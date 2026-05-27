// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Configuration;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.Logging;

/// <summary>
/// Default <see cref="ILoggingService"/> implementation. Writes to the debug output
/// and, when enabled, asynchronously appends formatted entries to a log file.
/// </summary>
public class LoggingService : ILoggingService, IDisposable
{
    private const string LogFolderName = "AetherAprs";
    private const string LogSubfolder = "Logs";
    private const string LogFileName = "app.log";

    private readonly LogLevel _minLogLevel;
    private readonly bool _writeToFile;
    private readonly string _logFilePath;
    private readonly BlockingCollection<string>? _writeQueue;
    private readonly Task? _writerTask;
    private readonly CancellationTokenSource? _cts;
    private bool _disposed;

    public LoggingService(IConfigurationService configurationService)
    {
        ArgumentNullException.ThrowIfNull(configurationService);

        var settings = configurationService.Settings;
        _minLogLevel = settings.Logging.LogLevel;
        _writeToFile = settings.Logging.WriteToFile;

        if (!_writeToFile)
        {
            _logFilePath = string.Empty;
            return;
        }

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logFilePath = Path.Combine(appData, LogFolderName, LogSubfolder, LogFileName);

            var directory = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _writeQueue = new BlockingCollection<string>(boundedCapacity: 1024);
            _cts = new CancellationTokenSource();
            _writerTask = Task.Run(() => ProcessQueueAsync(_cts.Token));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialise log file: {ex.Message}");
            _writeToFile = false;
            _logFilePath = string.Empty;
        }

        Info($"Logging started (minLevel={_minLogLevel}, writeToFile={_writeToFile}{(_writeToFile ? $", path={_logFilePath}" : string.Empty)})");
    }

    /// <inheritdoc/>
    public void Log(LogLevel level, string message)
    {
        if (level < _minLogLevel || _disposed)
        {
            return;
        }

        message ??= string.Empty;
        var formattedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        // Write to debug output for development; production builds strip this away.
        System.Diagnostics.Debug.WriteLine(formattedMessage);

        if (!_writeToFile || _writeQueue is null || _writeQueue.IsAddingCompleted)
        {
            return;
        }

        // Use TryAdd to avoid blocking the caller if the queue is full.
        if (!_writeQueue.TryAdd(formattedMessage))
        {
            System.Diagnostics.Debug.WriteLine("Log queue full; dropping log entry.");
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Information, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
    public void Critical(string message) => Log(LogLevel.Critical, message);

    public ILoggingService ForContext(string contextName) => new ContextLogger(this, contextName);

    private sealed class ContextLogger(ILoggingService inner, string contextName) : ILoggingService
    {
        private readonly string _prefix = $"[{contextName}] ";

        public void Log(LogLevel level, string message) => inner.Log(level, _prefix + message);
        public void Debug(string message) => inner.Log(LogLevel.Debug, _prefix + message);
        public void Info(string message) => inner.Log(LogLevel.Information, _prefix + message);
        public void Warn(string message) => inner.Log(LogLevel.Warning, _prefix + message);
        public void Error(string message) => inner.Log(LogLevel.Error, _prefix + message);
        public void Critical(string message) => inner.Log(LogLevel.Critical, _prefix + message);
        public ILoggingService ForContext(string nestedContextName) => new ContextLogger(inner, contextName + "/" + nestedContextName);
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        if (_writeQueue is null)
        {
            return;
        }

        try
        {
            foreach (var entry in _writeQueue.GetConsumingEnumerable(cancellationToken))
            {
                try
                {
                    await File.AppendAllTextAsync(
                        _logFilePath,
                        entry + Environment.NewLine,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Info("Logging stopping");

            _writeQueue?.CompleteAdding();

            try
            {
                // Wait briefly for the writer to drain remaining entries.
                _writerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Ignore exceptions during shutdown
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _writeQueue?.Dispose();
        }

        _disposed = true;
    }
}
