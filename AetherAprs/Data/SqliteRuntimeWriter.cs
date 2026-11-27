// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AetherAprs.Data;

/// <summary>Serializes transactions for typed persistence adapters in the Data layer.</summary>
internal sealed class SqliteRuntimeWriter : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// The retry budget for one write. Production uses <see cref="Default"/>;
    /// a test that exercises contention overrides it so it can observe the
    /// retry limit without waiting out the production lock budget.
    /// </summary>
    /// <remarks>
    /// The lock wait itself belongs to the connection and is configured by
    /// <see cref="AppDbContext.CreateOptions"/>; only the retry policy lives here.
    /// </remarks>
    internal sealed record Options
    {
        /// <summary>The production budget: a few retries, spaced out.</summary>
        public static readonly Options Default = new();

        /// <summary>
        /// Delay before re-attempting a transaction that never started, multiplied by
        /// the attempt number so retries back off. Each attempt first waits out the
        /// connection's busy timeout.
        /// </summary>
        public TimeSpan BeginRetryDelay { get; init; } = TimeSpan.FromMilliseconds(25);

        /// <summary>
        /// How many times a transaction that never started may be re-attempted on
        /// SQLITE_BUSY/LOCKED. Work is never replayed after a commit was attempted,
        /// so this only bounds pathological contention.
        /// </summary>
        public int MaxBeginAttempts { get; init; } = 5;
    }

    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly Channel<IWriteCommand> _commands;
    private readonly Lock _gate = new();
    private readonly Meter _meter = new("AetherAprs.Data.SqliteWriter");
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _retryCount;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _rejections;
    private readonly Options _options;
    private Task? _worker;
    private Task? _stop;

    public SqliteRuntimeWriter(
        IDbContextFactory<AppDbContext> dbContextFactory,
        int capacity = 128,
        Options? options = null)
    {
        _dbContextFactory = dbContextFactory;
        _options = options ?? Options.Default;
        _commands = Channel.CreateBounded<IWriteCommand>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        _meter.CreateObservableGauge("sqlite.writer.queue.depth", () => _commands.Reader.Count, "{command}");
        _duration = _meter.CreateHistogram<double>("sqlite.writer.operation.duration", "ms");
        _retryCount = _meter.CreateCounter<long>("sqlite.writer.retries", "{retry}");
        _failures = _meter.CreateCounter<long>("sqlite.writer.failures", "{failure}");
        _rejections = _meter.CreateCounter<long>("sqlite.writer.rejections", "{command}");
    }

    /// <summary>
    /// Admits one command, or rejects it immediately when the queue is full or closed.
    /// Commands must materialize their results and may save changes inside the writer's
    /// transaction. They must not commit, perform external side effects, or call the writer.
    /// The operation name is a fixed technical identifier suitable for diagnostics.
    /// </summary>
    internal Task<T> EnqueueAsync<T>(
        string operation,
        Func<AppDbContext, CancellationToken, Task<T>> execute,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(execute);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        lock (_gate)
        {
            if (_stop is not null)
            {
                return Task.FromException<T>(new ObjectDisposedException(nameof(SqliteRuntimeWriter)));
            }

            WriteCommand<T> command = new(operation, execute, cancellationToken);
            if (_commands.Writer.TryWrite(command))
            {
                _worker ??= Task.Run(ProcessAsync, CancellationToken.None);
                return command.Result;
            }

            command.Dispose();
        }

        Count(_rejections);
        return Task.FromException<T>(new SqliteWriteQueueFullException());
    }

    /// <summary>Closes admission and drains admitted commands. May be called repeatedly.</summary>
    internal Task StopAsync()
    {
        lock (_gate)
        {
            _commands.Writer.TryComplete();
            return _stop ??= FinishAsync(_worker ?? Task.CompletedTask);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    // Avalonia's synchronous disposal path must also drain admitted work. The
    // dedicated worker does not depend on the UI synchronization context.
    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    private async Task FinishAsync(Task worker)
    {
        try
        {
            await worker.ConfigureAwait(false);
        }
        finally
        {
            _meter.Dispose();
        }
    }

    private async Task ProcessAsync()
    {
        await foreach (IWriteCommand? command in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            using (command)
            {
                await command.RunAsync(this).ConfigureAwait(false);
            }
        }
    }

    private async Task<T> ExecuteAsync<T>(
        string operation, Func<AppDbContext, CancellationToken, Task<T>> execute, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool acquiredTransaction = false;
            bool committed = false;
            Exception? commitError = null;
            T result = default!;
            try
            {
                await using AppDbContext context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

                // SQLite honours the connection's busy timeout for BEGIN IMMEDIATE, so
                // lock contention surfaces as SQLITE_BUSY instead of a command timeout.
                await using IDbContextTransaction transaction = await context.Database
                    .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
                acquiredTransaction = true;

                result = await execute(context, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // The commit decision owns the outcome from here. Caller cancellation
                    // must not disguise a write that committed.
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                    committed = true;
                }
                catch (Exception ex)
                {
                    commitError = ex;
                    throw;
                }

                return result;
            }
            catch (Exception) when (committed)
            {
                // A provider cleanup failure cannot turn a known successful commit into a failed write.
                Count(_failures, new KeyValuePair<string, object?>("stage", "cleanup"));
                return result;
            }
            catch (Exception) when (commitError is not null)
            {
                throw new SqliteCommitOutcomeUnknownException(operation, commitError);
            }
            catch (SqliteException ex) when (!acquiredTransaction && attempt < _options.MaxBeginAttempts &&
                ex.SqliteErrorCode is 5 or 6)
            {
                // No command body ran. Re-enter with a fresh context/transaction; never
                // replay work after a commit was attempted, even on BUSY/LOCKED.
                Count(_retryCount);
                await Task.Delay(_options.BeginRetryDelay * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // MeterListener handlers execute inline. A failing observer must not stop
    // the worker or alter any command's commit or admission result.
    private static void Count(Counter<long> counter, KeyValuePair<string, object?>? tag = null)
    {
        try
        {
            if (tag is { } value)
            {
                counter.Add(1, value);
            }
            else
            {
                counter.Add(1);
            }
        }
        catch (Exception)
        {
        }
    }

    private void RecordDuration(long started)
    {
        try
        {
            _duration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception)
        {
        }
    }

    private interface IWriteCommand : IDisposable
    {
        Task RunAsync(SqliteRuntimeWriter writer);
    }

    private sealed class WriteCommand<T> : IWriteCommand
    {
        private readonly string _operation;
        private readonly Func<AppDbContext, CancellationToken, Task<T>> _execute;
        private readonly CancellationToken _cancellationToken;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;
        private int _state; // 0: queued; 1: executing; 2: cancelled before execution

        public WriteCommand(string operation, Func<AppDbContext, CancellationToken, Task<T>> execute,
            CancellationToken cancellationToken)
        {
            _operation = operation;
            _execute = execute;
            _cancellationToken = cancellationToken;
            _registration = cancellationToken.UnsafeRegister(static state => ((WriteCommand<T>)state!).CancelQueued(), this);
        }

        public Task<T> Result => _completion.Task;

        private void CancelQueued()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _completion.TrySetCanceled(_cancellationToken);
            }
        }

        public async Task RunAsync(SqliteRuntimeWriter writer)
        {
            if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            try
            {
                T? result = await writer.ExecuteAsync(_operation, _execute, _cancellationToken).ConfigureAwait(false);
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(_cancellationToken);
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
                Count(writer._failures);
            }
            finally
            {
                writer.RecordDuration(started);
            }
        }

        public void Dispose() => _registration.Dispose();
    }
}

/// <summary>A write was rejected before admission because the bounded queue was full.</summary>
internal sealed class SqliteWriteQueueFullException()
    : InvalidOperationException("The SQLite runtime write queue is full.");

/// <summary>The provider failed during commit and the write must not be blindly retried.</summary>
internal sealed class SqliteCommitOutcomeUnknownException(string operation, Exception innerException)
    : InvalidOperationException($"The SQLite commit outcome for '{operation}' is unknown.", innerException);
