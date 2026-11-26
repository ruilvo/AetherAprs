// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Threading.Tasks;

namespace AetherAprs.Services.Platform;

/// <summary>
/// No-op implementation of IForegroundService for desktop platforms.
/// </summary>
/// <remarks>
/// There is no foreground service outside Android: the process lifetime is the
/// keep-alive mechanism, so the requirement is already satisfied and a start is
/// reported as successful. <see cref="IsRunning"/> stays <see langword="false"/>
/// because no platform service object exists to report on.
/// </remarks>
public sealed class NoOpForegroundService : IForegroundService
{
    public bool IsRunning => false;

    public Task<ForegroundServiceStartResult> StartAsync(ForegroundServiceType requiredTypes) =>
        Task.FromResult(ForegroundServiceStartResult.Started());

    public Task StopAsync() => Task.CompletedTask;
}
