// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;

namespace AetherAprs.Data;

/// <summary>
/// Singleton row for the currently selected dynamic beaconing mode.
/// </summary>
public sealed class BeaconingStateRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public DynamicBeaconMode ActiveMode { get; set; } = DynamicBeaconMode.Walk;
}
