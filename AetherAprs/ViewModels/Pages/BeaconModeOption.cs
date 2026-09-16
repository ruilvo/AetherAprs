// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.ViewModels.Pages;

public sealed record BeaconModeOption(DynamicBeaconMode? Mode, string DisplayName)
{
    public static BeaconModeOption UseDefault { get; } = new(null, "<Use default>");

    public static IReadOnlyList<BeaconModeOption> All { get; } =
    [
        UseDefault,
        new(DynamicBeaconMode.Walk, "Walk"),
        new(DynamicBeaconMode.Drive, "Drive"),
        new(DynamicBeaconMode.Custom, "Custom")
    ];

    public static BeaconModeOption FromMode(DynamicBeaconMode? mode) =>
        All.First(option => option.Mode == mode);
}
