// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Configuration.Settings.Documents;

/// <summary>
/// Mutable binding target for the <c>Map</c> settings section. See
/// <see cref="AppSettingsDocument"/> for why this shape exists.
/// </summary>
internal sealed class MapSettingsDocument
{
    public TileSourceType TileSource { get; set; } = TileSourceType.OsmOnline;

    public string MbTilesFilePath { get; set; } = string.Empty;

    public MapSettings ToSettings() => new()
    {
        TileSource = TileSource,
        MbTilesFilePath = MbTilesFilePath,
    };
}
