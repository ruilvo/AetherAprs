// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    private const int MaxBeginAttempts = 3;
    private const int CommandTimeoutSeconds = 1;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly Channel<IWriteCommand> _commands;
    private readonly Lock _gate = new();
    private readonly Meter _meter = new("AetherAprs.Data.SqliteWriter");
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _retryCount;
    private readonly Counter<long> _failures;
    private readonly Counter<long> _rejections;
    private Task? _worker;
    private Task? _stop;

    public SqliteRuntimeWriter(IDbContextFactory<AppDbContext> dbContextFactory, int capacity = 128)
    {
        _dbContextFactory = dbContextFactory;
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

            var command = new WriteCommand<T>(operation, execute, cancellationToken);
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
        await foreach (var command in _commands.Reader.ReadAllAsync().ConfigureAwait(false))
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
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var acquiredTransaction = false;
            var committed = false;
            Exception? commitError = null;
            T result = default!;
            try
            {
                await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
                context.Database.SetCommandTimeout(CommandTimeoutSeconds);
                // SQLite uses its connection timeout for BEGIN IMMEDIATE, separately from EF command timeouts.
                ((SqliteConnection)context.Database.GetDbConnection()).DefaultTimeout = CommandTimeoutSeconds;
                await using var transaction = await context.Database
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
            catch (SqliteException ex) when (!acquiredTransaction && attempt < MaxBeginAttempts &&
                ex.SqliteErrorCode is 5 or 6)
            {
                // No command body ran. Re-enter with a fresh context/transaction; never
                // replay work after a commit was attempted, even on BUSY/LOCKED.
                Count(_retryCount);
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken).ConfigureAwait(false);
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

            var started = Stopwatch.GetTimestamp();
            try
            {
                var result = await writer.ExecuteAsync(_operation, _execute, _cancellationToken).ConfigureAwait(false);
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
