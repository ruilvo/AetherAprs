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
        using var database = TempAppDatabase.CreateEmpty();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(database.Factory);
        services.AddSingleton<AppSavedDataInitializer>();
        services.AddSqliteRuntime();
        using var provider = services.BuildServiceProvider();

        var reader = provider.GetRequiredService<SqliteRuntimeReader>();
        var writer = provider.GetRequiredService<SqliteRuntimeWriter>();

        Assert.Same(reader, provider.GetRequiredService<SqliteRuntimeReader>());
        Assert.Same(writer, provider.GetRequiredService<SqliteRuntimeWriter>());
        var cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(3, await reader.QueryAsync((context, ct) => context.BeaconConfigs.CountAsync(ct), cancellationToken));
        Assert.True(await writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0ONE", ct), cancellationToken) > 0);
    }

    [Fact]
    public async Task WritesCommitInAdmissionOrder_QueriesDoNotWaitForUncommittedWriteOrEachOther()
    {
        using var database = TempAppDatabase.Create();
        await using var writer = new SqliteRuntimeWriter(database.Factory);
        var reader = new SqliteRuntimeReader(database.Factory);
        using var activeWrite = new Gate();
        using var activeRead = new Gate();
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            var id = await InsertAsync(context, "N0ONE", ct);
            await activeWrite.EnterAsync(ct);
            return id;
        }, cancellationToken);
        await activeWrite.WaitForEntryAsync(cancellationToken);
        var heldRead = reader.QueryAsync(async (context, ct) =>
        {
            var rows = await context.Packets.CountAsync(ct);
            await activeRead.EnterAsync(ct);
            return rows;
        }, cancellationToken);
        await activeRead.WaitForEntryAsync(cancellationToken);

        Assert.Empty(await ReadSourcesAsync(reader, cancellationToken).WaitAsync(TestTimeouts.TestTimeout, cancellationToken));
        var second = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            Assert.Equal("N0ONE", await context.Packets.Select(packet => packet.SourceBase).SingleAsync(ct));
            return await InsertAsync(context, "N0TWO", ct);
        }, cancellationToken);
        Assert.False(second.IsCompleted);

        activeWrite.Release();
        var ids = await Task.WhenAll(first, second).WaitAsync(TestTimeouts.TestTimeout, cancellationToken);
        Assert.True(ids[0] < ids[1]);
        Assert.Equal(["N0ONE", "N0TWO"], await ReadSourcesAsync(reader, cancellationToken));
        activeRead.Release();
        Assert.Equal(0, await heldRead);
    }

    [Fact]
    public async Task FullQueueRejectsAndStopDrainsAdmittedWork()
    {
        using var database = TempAppDatabase.Create();
        await using var writer = new SqliteRuntimeWriter(database.Factory, capacity: 1);
        using var activeWrite = new Gate();
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            await activeWrite.EnterAsync(ct);
            return await InsertAsync(context, "N0ONE", ct);
        }, cancellationToken);
        await activeWrite.WaitForEntryAsync(cancellationToken);
        var second = writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0TWO", ct), cancellationToken);

        await Assert.ThrowsAsync<SqliteWriteQueueFullException>(() => writer.EnqueueAsync("test.insert",
            (context, ct) => InsertAsync(context, "N0FULL", ct), cancellationToken));
        var stopped = writer.StopAsync();
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
        using var database = TempAppDatabase.Create();
        await using var writer = new SqliteRuntimeWriter(database.Factory);
        using var activeWrite = new Gate();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var queuedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var first = writer.EnqueueAsync("test.insert", async (context, ct) =>
        {
            var id = await InsertAsync(context, "N0ONE", ct);
            await activeWrite.EnterAsync(ct);
            return id;
        }, activeCancellation.Token);
        await activeWrite.WaitForEntryAsync(cancellationToken);
        var second = writer.EnqueueAsync<long>("test.insert",
            (_, _) => throw new InvalidOperationException("A cancelled command ran."), queuedCancellation.Token);
        var third = writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0THREE", ct), cancellationToken);

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
        using var database = TempAppDatabase.Create();
        using var committing = new Gate();
        var interceptor = new TestTransactionInterceptor { OnCommitting = committing.EnterAsync };
        await using var writer = new SqliteRuntimeWriter(CreateFactory(database, interceptor));
        var cancellationToken = TestContext.Current.CancellationToken;
        using var callerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var command = writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0ONE", ct), callerCancellation.Token);
        await committing.WaitForEntryAsync(cancellationToken);
        await callerCancellation.CancelAsync();
        committing.Release();

        Assert.True(await command.WaitAsync(TestTimeouts.TestTimeout, cancellationToken) > 0);
        Assert.Equal(["N0ONE"], await ReadSourcesAsync(new SqliteRuntimeReader(database.Factory), cancellationToken));
    }

    [Fact]
    public async Task RollbackAndIndeterminateCommit_DoNotPoisonNextCommandOrReplay()
    {
        using var database = TempAppDatabase.Create();
        var failed = new InvalidOperationException("Synthetic command failure.");
        var commitError = new SqliteException("Synthetic lost commit acknowledgement.", 5);
        var commitAttempts = 0;
        var interceptor = new TestTransactionInterceptor
        {
            OnCommitted = () => ++commitAttempts == 1 ? Task.FromException(commitError) : Task.CompletedTask
        };
        await using var writer = new SqliteRuntimeWriter(CreateFactory(database, interceptor));
        var cancellationToken = TestContext.Current.CancellationToken;

        var rollback = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.EnqueueAsync<long>("test.rollback",
            async (context, ct) =>
            {
                await InsertAsync(context, "N0FAIL", ct);
                throw failed;
            }, cancellationToken));
        Assert.Same(failed, rollback);
        var unknown = await Assert.ThrowsAsync<SqliteCommitOutcomeUnknownException>(() => writer.EnqueueAsync("test.insert",
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
        using var database = TempAppDatabase.Create();
        var attempts = 0;
        var interceptor = new TestTransactionInterceptor
        {
            OnStarting = () => attempts++
        };
        var factory = CreateFactory(database, interceptor);
        await using var writer = new SqliteRuntimeWriter(factory);
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var contendingContext = database.CreateContext())
        await using (await contendingContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() => writer.EnqueueAsync<long>("test.busy",
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
        using var database = TempAppDatabase.Create();
        await using var writer = new SqliteRuntimeWriter(database.Factory);
        var reader = new SqliteRuntimeReader(database.Factory);
        var cancellationToken = TestContext.Current.CancellationToken;
        await writer.EnqueueAsync("test.insert", (context, ct) => InsertAsync(context, "N0ONE", ct), cancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.QueryAsync(
            (context, _) => Task.FromResult(context.Packets.Where(packet => packet.Id > 0)), cancellationToken));
        var failure = await Assert.ThrowsAsync<SqliteException>(() => reader.QueryAsync(
            (context, ct) => context.Packets.ExecuteDeleteAsync(ct), cancellationToken));
        Assert.Equal(8, failure.SqliteErrorCode); // SQLITE_READONLY
        Assert.Equal(["N0ONE"], await ReadSourcesAsync(reader, cancellationToken));
    }

    private static async Task<long> InsertAsync(AppDbContext context, string source, CancellationToken cancellationToken)
    {
        var record = new PacketRecord
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
