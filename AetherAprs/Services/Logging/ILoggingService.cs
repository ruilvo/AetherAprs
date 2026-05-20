// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Logging;

/// <summary>
/// Defines the severity levels for log messages.
/// </summary>
public enum LogLevel
{
    /// <summary>Verbose diagnostic information, typically only useful during development.</summary>
    Debug,
    /// <summary>General operational information about application flow.</summary>
    Information,
    /// <summary>An unexpected event that does not prevent the application from continuing.</summary>
    Warning,
    /// <summary>A failure in the current operation that may be recoverable.</summary>
    Error,
    /// <summary>A fatal or unrecoverable condition requiring immediate attention.</summary>
    Critical
}

/// <summary>
/// Provides application-wide logging capabilities with configurable log levels.
/// </summary>
public interface ILoggingService
{
    /// <summary>
    /// Logs a message at the specified level.
    /// </summary>
    void Log(LogLevel level, string message);

    /// <summary>
    /// Logs a message at the Debug level.
    /// </summary>
    void Debug(string message);

    /// <summary>
    /// Logs a message at the Information level.
    /// </summary>
    void Info(string message);

    /// <summary>
    /// Logs a message at the Warning level.
    /// </summary>
    void Warn(string message);

    /// <summary>
    /// Logs a message at the Error level.
    /// </summary>
    void Error(string message);

    /// <summary>
    /// Logs a message at the Critical level, indicating a fatal or unrecoverable condition.
    /// </summary>
    void Critical(string message);
}
