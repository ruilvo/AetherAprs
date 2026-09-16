// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Localization;
using System.Collections.Generic;
using System.Linq;

namespace AetherAprs.ViewModels.Pages;

public sealed record BeaconModeOption(DynamicBeaconMode? Mode)
{
    public string DisplayName => Mode switch
    {
        null => Strings.Get("UseDefaultBeaconMode"),
        DynamicBeaconMode.Walk => Strings.Get("BeaconModeWalk"),
        DynamicBeaconMode.Drive => Strings.Get("BeaconModeDrive"),
        DynamicBeaconMode.Custom => Strings.Get("BeaconModeCustom"),
        _ => Mode.ToString() ?? string.Empty
    };

    public static BeaconModeOption UseDefault { get; } = new((DynamicBeaconMode?)null);

    public static IReadOnlyList<BeaconModeOption> All { get; } =
    [
        UseDefault,
        new(DynamicBeaconMode.Walk),
        new(DynamicBeaconMode.Drive),
        new(DynamicBeaconMode.Custom)
    ];

    public static BeaconModeOption FromMode(DynamicBeaconMode? mode) =>
        All.First(option => option.Mode == mode);
}
