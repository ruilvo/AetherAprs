// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android.Util;
using Microsoft.Extensions.Logging;
using System;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Writes log records to logcat so framework entry points keep producing output
/// before the dependency-injection container exists.
/// </summary>
/// <remarks>
/// This is the only place in the Android project that names <c>Android.Util.Log</c>.
/// Activities, the application class and the platform adapters log through
/// <see cref="ApplicationLog"/> instead, which hands them the application
/// container's <see cref="ILoggerFactory"/> once Avalonia has started.
/// </remarks>
public sealed class AndroidLogcatLoggerProvider : ILoggerProvider
{
    private const string Tag = "AetherAprs";

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new LogcatLogger(categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
        // Logcat needs no handle to release.
    }

    private sealed class LogcatLogger(string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // The formatter already folds the exception into the message, and
            // logcat has no structured-field support, so the rendered line is
            // the record. Callers still pass values as structured arguments, so
            // the same message is structured wherever a richer provider is
            // attached.
            var message = $"{categoryName}: {formatter(state, exception)}";

            switch (logLevel)
            {
                case LogLevel.Critical:
                    global::Android.Util.Log.Wtf(Tag, message);
                    break;
                case LogLevel.Error:
                    global::Android.Util.Log.Error(Tag, message);
                    break;
                case LogLevel.Warning:
                    global::Android.Util.Log.Warn(Tag, message);
                    break;
                case LogLevel.Information:
                    global::Android.Util.Log.Info(Tag, message);
                    break;
                default:
                    global::Android.Util.Log.Debug(Tag, message);
                    break;
            }
        }
    }
}
