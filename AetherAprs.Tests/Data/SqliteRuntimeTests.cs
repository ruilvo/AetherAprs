// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Data;
using AetherAprs.Data.Entities;
using AetherAprs.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class SqliteRuntimeTests
{
    [Fact]
    public async Task Registration_BootstrapsBeforeRuntimeAndReturnsSingletons()
    {
        using TempAppDatabase database = TempAppDatabase.CreateEmpty();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(database.Factory);
        services.AddSingleton<AppSavedDataInitializer>();
        services.AddSqliteRuntime();
        using ServiceProvider provider = services.BuildServiceProvider();

        SqliteRuntimeReader reader = provider.GetRequiredService<SqliteRuntimeReader>();
        SqliteRuntimeWriter writer = provider.GetRequiredService<SqliteRuntimeWriter>();

        Assert.Same(reader, provider.GetRequiredService<SqliteRuntimeReader>());
        Assert.Same(writer, provider.GetRequiredService<SqliteRuntimeWriter>());
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(3, await reader.QueryAsync((context, ct) => context.BeaconConfigs.CountAsync(ct), cancellationToken));
        Assert.True(await writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0ONE", ct), cancellationToken) > 0);
    }

    [Fact]
    public async Task WritesCommitInAdmissionOrder_QueriesDoNotWaitForUncommittedWriteOrEachOther()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        await using SqliteRuntimeWriter writer = new(database.Factory);
        SqliteRuntimeReader reader = new(database.Factory);
        using Gate activeWrite = new();
        using Gate activeRead = new();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<long> first = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            long id = await InsertAsync(context, "N0ONE", ct);
            await activeWrite.EnterAsync(ct);
            return id;
        }, cancellationToken);
        await activeWrite.WaitForEntryAsync(cancellationToken);
        Task<int> heldRead = reader.QueryAsync(async (context, ct) =>
        {
            int rows = await context.Packets.CountAsync(ct);
            await activeRead.EnterAsync(ct);
            return rows;
        }, cancellationToken);
        await activeRead.WaitForEntryAsync(cancellationToken);

        Assert.Empty(await ReadSourcesAsync(reader, cancellationToken).WaitAsync(TestTimeouts.TestTimeout, cancellationToken));
        Task<long> second = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            Assert.Equal("N0ONE", await context.Packets.Select(packet => packet.SourceBase).SingleAsync(ct));
            return await InsertAsync(context, "N0TWO", ct);
        }, cancellationToken);
        Assert.False(second.IsCompleted);

        activeWrite.Release();
        long[] ids = await Task.WhenAll(first, second).WaitAsync(TestTimeouts.TestTimeout, cancellationToken);
        Assert.True(ids[0] < ids[1]);
        Assert.Equal(["N0ONE", "N0TWO"], await ReadSourcesAsync(reader, cancellationToken));
        activeRead.Release();
        Assert.Equal(0, await heldRead);
    }

    [Fact]
    public async Task FullQueueRejectsAndStopDrainsAdmittedWork()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        await using SqliteRuntimeWriter writer = new(database.Factory, capacity: 1);
        using Gate activeWrite = new();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        Task<long> first = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            await activeWrite.EnterAsync(ct);
            return await InsertAsync(context, "N0ONE", ct);
        }, cancellationToken);
        await activeWrite.WaitForEntryAsync(cancellationToken);
        Task<long> second = writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0TWO", ct), cancellationToken);

        await Assert.ThrowsAsync<SqliteWriteQueueFullException>(() => writer.EnqueueAsync("test.insert",
            (context, ct) => InsertAsync(context, "N0FULL", ct), cancellationToken));
        Task stopped = writer.StopAsync();
        Assert.False(stopped.IsCompleted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.EnqueueAsync("test.insert",
            (context, ct) => InsertAsync(context, "N0STOP", ct), cancellationToken));
        activeWrite.Release();

        await stopped.WaitAsync(TestTimeouts.TestTimeout, cancellationToken);
        Assert.Same(stopped, writer.StopAsync());
        Assert.All(await Task.WhenAll(first, second), id => Assert.True(id > 0));
        Assert.Equal(["N0ONE", "N0TWO"], await ReadSourcesAsync(new SqliteRuntimeReader(database.Factory), cancellationToken));
    }

    [Fact]
    public async Task CancellationSkipsQueuedCommandsAndRollsBackUncommittedWrites()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        await using SqliteRuntimeWriter writer = new(database.Factory);
        using Gate activeWrite = new();
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource queuedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<long> first = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            long id = await InsertAsync(context, "N0ONE", ct);
            await activeWrite.EnterAsync(ct);
            return id;
        }, activeCancellation.Token);
        await activeWrite.WaitForEntryAsync(cancellationToken);
        Task<long> second = writer.EnqueueAsync<long>("test.insert",
            (_, _) => throw new InvalidOperationException("A cancelled command ran."), queuedCancellation.Token);
        Task<long> third = writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0THREE", ct), cancellationToken);

        await queuedCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TestTimeouts.TestTimeout, cancellationToken));
        await activeCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TestTimeouts.TestTimeout, cancellationToken));

        Assert.True(await third.WaitAsync(TestTimeouts.TestTimeout, cancellationToken) > 0);
        Assert.Equal(["N0THREE"], await ReadSourcesAsync(new SqliteRuntimeReader(database.Factory), cancellationToken));
    }

    [Fact]
    public async Task CancellationDuringCommitReturnsCommittedResult()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        using Gate committing = new();
        TestTransactionInterceptor interceptor = new() { OnCommitting = committing.EnterAsync };
        await using SqliteRuntimeWriter writer = new(CreateFactory(database, interceptor));
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<long> command = writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0ONE", ct), callerCancellation.Token);
        await committing.WaitForEntryAsync(cancellationToken);
        await callerCancellation.CancelAsync();
        committing.Release();

        Assert.True(await command.WaitAsync(TestTimeouts.TestTimeout, cancellationToken) > 0);
        Assert.Equal(["N0ONE"], await ReadSourcesAsync(new SqliteRuntimeReader(database.Factory), cancellationToken));
    }

    [Fact]
    public async Task RollbackAndIndeterminateCommit_DoNotPoisonNextCommandOrReplay()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        InvalidOperationException failed = new("Synthetic command failure.");
        SqliteException commitError = new("Synthetic lost commit acknowledgement.", 5);
        int commitAttempts = 0;
        TestTransactionInterceptor interceptor = new()
        {
            OnCommitted = () => ++commitAttempts == 1 ? Task.FromException(commitError) : Task.CompletedTask
        };
        await using SqliteRuntimeWriter writer = new(CreateFactory(database, interceptor));
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        InvalidOperationException rollback = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.EnqueueAsync<long>("test.rollback",
            async (context, ct) =>
            {
                await InsertAsync(context, "N0FAIL", ct);
                throw failed;
            }, cancellationToken));
        Assert.Same(failed, rollback);
        SqliteCommitOutcomeUnknownException unknown = await Assert.ThrowsAsync<SqliteCommitOutcomeUnknownException>(() => writer.EnqueueAsync("test.insert",
            (context, ct) => InsertAsync(context, "N0MAYBE", ct), cancellationToken));
        Assert.Same(commitError, unknown.InnerException);
        Assert.Equal(1, commitAttempts);
        Assert.True(await writer.EnqueueAsync("test.insert",
            (context, ct) => InsertAsync(context, "N0OK", ct), cancellationToken) > 0);
        Assert.Equal(["N0MAYBE", "N0OK"], await ReadSourcesAsync(new SqliteRuntimeReader(database.Factory), cancellationToken));
    }

    [Fact]
    public async Task BusyOnBeginUsesFreshContextsAndStopsAtRetryLimit()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        int attempts = 0;
        TestTransactionInterceptor interceptor = new()
        {
            OnStarting = () => attempts++
        };
        InterceptedContextFactory factory = CreateFactory(database, interceptor);
        await using SqliteRuntimeWriter writer = new(factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using (AppDbContext contendingContext = database.CreateContext())
        await using (await contendingContext.Database.BeginTransactionAsync(cancellationToken))
        {
            SqliteException error = await Assert.ThrowsAsync<SqliteException>(() => writer.EnqueueAsync<long>("test.busy",
                    (_, _) => throw new InvalidOperationException("Command ran without a transaction."), cancellationToken)
                .WaitAsync(TestTimeouts.TestTimeout, cancellationToken));
            Assert.Equal(5, error.SqliteErrorCode); // SQLITE_BUSY
            Assert.Equal(3, attempts);
            Assert.Equal(3, factory.ContextsCreated);
        }

        Assert.True(await writer.EnqueueAsync("test.insert",
            (context, ct) => InsertAsync(context, "N0OK", ct), cancellationToken) > 0);
        Assert.Equal(4, factory.ContextsCreated);
    }

    [Fact]
    public async Task ReaderRejectsDeferredResultsAndCannotMutateTheDatabase()
    {
        using TempAppDatabase database = TempAppDatabase.Create();
        await using SqliteRuntimeWriter writer = new(database.Factory);
        SqliteRuntimeReader reader = new(database.Factory);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0ONE", ct), cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.QueryAsync(
            (context, _) => Task.FromResult(context.Packets.Where(packet => packet.Id > 0)), cancellationToken));
        SqliteException failure = await Assert.ThrowsAsync<SqliteException>(() => reader.QueryAsync(
            (context, ct) => context.Packets.ExecuteDeleteAsync(ct), cancellationToken));
        Assert.Equal(8, failure.SqliteErrorCode); // SQLITE_READONLY
        Assert.Equal(["N0ONE"], await ReadSourcesAsync(reader, cancellationToken));
    }

    private static async Task<long> InsertAsync(AppDbContext context, string source, CancellationToken cancellationToken)
    {
        PacketRecord record = new()
        {
            SourceBase = source,
            DestinationBase = "APRS",
            Timestamp = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc)
        };
        context.Packets.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    private static Task<string[]> ReadSourcesAsync(SqliteRuntimeReader reader, CancellationToken cancellationToken) =>
        reader.QueryAsync((context, ct) => context.Packets.OrderBy(packet => packet.Id)
            .Select(packet => packet.SourceBase).ToArrayAsync(ct), cancellationToken);

    private static InterceptedContextFactory CreateFactory(TempAppDatabase database, TestTransactionInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<AppDbContext>(
            AppDbContext.CreateOptions(Path.Combine(database.Directory, AppDbContext.DatabaseFileName), pooling: false))
            .AddInterceptors(interceptor).Options);

    private sealed class InterceptedContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public int ContextsCreated { get; private set; }

        public AppDbContext CreateDbContext()
        {
            ContextsCreated++;
            return new AppDbContext(options);
        }
    }

    private sealed class Gate : IDisposable
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task EnterAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            return _released.Task.WaitAsync(TestTimeouts.TestTimeout, cancellationToken);
        }

        public Task WaitForEntryAsync(CancellationToken cancellationToken) =>
            _entered.Task.WaitAsync(TestTimeouts.TestTimeout, cancellationToken);

        public void Release() => _released.TrySetResult();

        public void Dispose() => Release();
    }

    private sealed class TestTransactionInterceptor : DbTransactionInterceptor
    {
        public Action? OnStarting { get; init; }
        public Func<CancellationToken, Task>? OnCommitting { get; init; }
        public Func<Task>? OnCommitted { get; init; }

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            OnStarting?.Invoke();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (OnCommitting is not null)
            {
                await OnCommitting(cancellationToken);
            }

            return result;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            OnCommitted?.Invoke() ?? Task.CompletedTask;
    }
}
