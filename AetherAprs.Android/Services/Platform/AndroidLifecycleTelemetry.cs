// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;

namespace AetherAprs.Android.Services.Platform;

internal static class AndroidLifecycleTelemetry
{
    internal static AndroidLifecycleState State { get; } = new();
}
