// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Logging;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public interface ILoggingService
{
    void Log(LogLevel level, string message);
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
