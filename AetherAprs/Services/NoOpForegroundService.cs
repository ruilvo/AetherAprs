// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services;

/// <summary>
/// No-op implementation of IForegroundService for desktop platforms.
/// </summary>
public sealed class NoOpForegroundService : IForegroundService
{
    public bool IsRunning => false;

    public Task StartAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;
}
