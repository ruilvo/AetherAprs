// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;

namespace AetherAprs.Services.UI;

/// <summary>
/// Reports the free space available where an offline tile package will be written.
/// Implementations exist so storage-quota handling can be tested without depending on
/// the machine's real disks.
/// </summary>
public interface IFreeSpaceProvider
{
    /// <summary>Returns the bytes available in the volume that holds <paramref name="directoryPath"/>.</summary>
    /// <param name="directoryPath">An existing directory that will receive the package.</param>
    /// <returns>The available bytes, or <see cref="long.MaxValue"/> when it cannot be determined.</returns>
    long GetAvailableBytes(string directoryPath);
}

/// <summary>
/// Reports free space from the file system drive that holds a directory.
/// </summary>
public sealed class DriveFreeSpaceProvider : IFreeSpaceProvider
{
    /// <inheritdoc />
    public long GetAvailableBytes(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        try
        {
            string fullPath = Path.GetFullPath(directoryPath);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return long.MaxValue;
            }

            DriveInfo drive = new(root);
            return drive.IsReady ? drive.AvailableFreeSpace : long.MaxValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Without a reliable figure the download proceeds; the write itself still
            // fails cleanly if the volume is genuinely full.
            return long.MaxValue;
        }
    }
}
