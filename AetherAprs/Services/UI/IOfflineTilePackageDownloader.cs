// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Services.UI;

/// <summary>
/// A geographic region, in WGS84 degrees, to pre-download for offline use.
/// </summary>
public sealed record OfflineDownloadRegion(
    double MinLongitude,
    double MinLatitude,
    double MaxLongitude,
    double MaxLatitude,
    int MinZoom,
    int MaxZoom);

/// <summary>
/// Progress reported while an offline tile package is being downloaded.
/// </summary>
public sealed record OfflineDownloadProgress(int CompletedTiles, int TotalTiles);

/// <summary>
/// Downloads OpenStreetMap tiles for a region into an MBTiles package for offline use.
/// </summary>
public interface IOfflineTilePackageDownloader
{
    /// <summary>
    /// Downloads every tile covering <paramref name="region"/> into a new MBTiles package at
    /// <paramref name="outputPath"/>. Any existing file at the path is overwritten.
    /// </summary>
    /// <param name="region">The region and zoom range to download.</param>
    /// <param name="outputPath">Where to write the <c>.mbtiles</c> package.</param>
    /// <param name="progress">Optional progress reporting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DownloadRegionAsync(
        OfflineDownloadRegion region,
        string outputPath,
        IProgress<OfflineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
