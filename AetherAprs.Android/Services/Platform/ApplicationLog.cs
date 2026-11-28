// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Supplies loggers to the Android entry points that the framework instantiates
/// without constructor injection.
/// </summary>
/// <remarks>
/// Android creates the application and activity classes itself, so they cannot
/// receive <see cref="ILogger{T}"/> through a constructor. This accessor resolves
/// the container's factory once it exists and falls back to logcat before that,
/// which keeps bootstrap failures visible. Everything that can take an injected
/// logger still should: this exists only for framework-owned objects.
/// </remarks>
public static class ApplicationLog
{
    private static readonly ILoggerFactory Logcat =
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddProvider(new AndroidLogcatLoggerProvider());
        });

    /// <summary>Creates a logger named after the requesting type.</summary>
    /// <typeparam name="T">The type whose category name is used.</typeparam>
    public static ILogger<T> For<T>() => Factory.CreateLogger<T>();

    /// <summary>Creates a logger for an explicit category name.</summary>
    /// <param name="categoryName">The category shown in the log record.</param>
    public static ILogger For(string categoryName) => Factory.CreateLogger(categoryName);

    private static ILoggerFactory Factory
    {
        get
        {
            ILoggerFactory? factory = Volatile.Read(ref field);
            if (factory != null)
            {
                return factory;
            }

            // The provider is unset until Avalonia finishes framework
            // initialization, and an activity can log before that.
            IServiceProvider? provider = (Avalonia.Application.Current as AetherAprs.App)?.ServiceProvider;
            if (provider?.GetService(typeof(ILoggerFactory)) is ILoggerFactory resolved)
            {
                Volatile.Write(ref field, resolved);
                return resolved;
            }

            return Logcat;
        }
    }
}
