// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;

namespace AetherAprs.Services.UI;

/// <summary>
/// Why the requested offline tile source was replaced by the online one.
/// </summary>
public enum MapTileSourceFallbackReason
{
    /// <summary>The configured source is in use; no fallback happened.</summary>
    None,

    /// <summary>The offline source was selected but no package path was configured.</summary>
    NotConfigured,

    /// <summary>The configured package file does not exist.</summary>
    FileMissing,

    /// <summary>The configured package exists but is not a usable MBTiles package.</summary>
    PackageCorrupt
}

/// <summary>
/// The outcome of the last <see cref="IMapTileSourceService.CreateBaseMapLayer"/> call.
/// Callers can inspect this to tell the operator when an offline package was rejected
/// instead of the failure being confined to the log.
/// </summary>
/// <param name="ActiveSource">The source the returned layer actually uses.</param>
/// <param name="FallbackReason">Why the configured source was not used, if it was not.</param>
/// <param name="Detail">The package path or diagnostic text, when available.</param>
public sealed record MapTileSourceStatus(
    TileSourceType ActiveSource,
    MapTileSourceFallbackReason FallbackReason,
    string? Detail)
{
    /// <summary>Whether the returned layer differs from the configured tile source.</summary>
    public bool IsFallback => FallbackReason != MapTileSourceFallbackReason.None;
}
