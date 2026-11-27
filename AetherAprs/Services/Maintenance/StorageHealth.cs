// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Globalization;

namespace AetherAprs.Services.Maintenance;

/// <summary>
/// How much room the volume holding local data has left.
/// </summary>
public enum StorageHealthLevel
{
    /// <summary>The free space could not be determined.</summary>
    Unknown,

    /// <summary>There is enough room for history and caches to keep growing.</summary>
    Healthy,

    /// <summary>Space is running out; retention and cache work should shrink.</summary>
    Warning,

    /// <summary>A write is expected to fail; the operator has to free space.</summary>
    Critical,
}

/// <summary>
/// One observation of the free space available for local application data.
/// </summary>
/// <param name="Level">The severity derived from <paramref name="AvailableBytes"/>.</param>
/// <param name="AvailableBytes">The bytes available, or <see langword="null"/> when unknown.</param>
/// <param name="WarningThresholdBytes">The bound at or below which the level is <see cref="StorageHealthLevel.Warning"/>.</param>
/// <param name="CriticalThresholdBytes">The bound at or below which the level is <see cref="StorageHealthLevel.Critical"/>.</param>
/// <param name="DirectoryPath">The directory the observation was taken for.</param>
/// <param name="CheckedAtUtc">When the observation was taken.</param>
public sealed record StorageHealthSnapshot(
    StorageHealthLevel Level,
    long? AvailableBytes,
    long WarningThresholdBytes,
    long CriticalThresholdBytes,
    string? DirectoryPath,
    DateTimeOffset CheckedAtUtc)
{
    /// <summary>Gets a snapshot for a volume whose free space is unknown.</summary>
    public static StorageHealthSnapshot Indeterminate { get; } = new(
        StorageHealthLevel.Unknown,
        null,
        0,
        0,
        null,
        DateTimeOffset.MinValue);

    /// <summary>
    /// Gets whether the free space is low enough that the operator should act.
    /// </summary>
    public bool NeedsAttention => Level is StorageHealthLevel.Warning or StorageHealthLevel.Critical;
}

/// <summary>
/// Formats byte counts for display without depending on a platform formatter.
/// </summary>
public static class StorageSizeFormatter
{
    private const long Kilobyte = 1024;
    private const long Megabyte = Kilobyte * 1024;
    private const long Gigabyte = Megabyte * 1024;

    /// <summary>
    /// Formats <paramref name="bytes"/> as a short base-1024 size such as
    /// <c>512 KB</c> or <c>6.3 GB</c>.
    /// </summary>
    /// <param name="bytes">The byte count; negative and zero values format as <c>0 KB</c>.</param>
    /// <param name="culture">The culture for the decimal separator; defaults to the invariant culture.</param>
    /// <returns>The formatted size.</returns>
    public static string Format(long bytes, CultureInfo? culture = null)
    {
        CultureInfo effectiveCulture = culture ?? CultureInfo.InvariantCulture;

        if (bytes >= Gigabyte)
        {
            return string.Format(effectiveCulture, "{0:0.#} GB", bytes / (double)Gigabyte);
        }

        if (bytes >= Megabyte)
        {
            return string.Format(effectiveCulture, "{0:0.#} MB", bytes / (double)Megabyte);
        }

        return string.Format(
            effectiveCulture,
            "{0:0} KB",
            Math.Max(0, bytes) / (double)Kilobyte);
    }
}
