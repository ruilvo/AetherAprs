// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.UI;

/// <summary>
/// Thrown before an offline tile download starts when the destination volume does not
/// have enough free space for the estimated package.
/// </summary>
public sealed class InsufficientStorageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="InsufficientStorageException"/> class.</summary>
    public InsufficientStorageException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The failure description.</param>
    public InsufficientStorageException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and an inner failure.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying failure.</param>
    public InsufficientStorageException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance with the quota figures that caused the refusal.</summary>
    /// <param name="directoryPath">The destination directory that was checked.</param>
    /// <param name="requiredBytes">The estimated bytes needed for the package.</param>
    /// <param name="availableBytes">The bytes reported free before the download.</param>
    public InsufficientStorageException(string directoryPath, long requiredBytes, long availableBytes)
        : base(
            $"Not enough free space to download the offline map package. "
            + $"Required approximately {requiredBytes} bytes but only {availableBytes} are available in '{directoryPath}'.")
    {
        DirectoryPath = directoryPath;
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
    }

    /// <summary>The destination directory that was checked, or an empty string.</summary>
    public string DirectoryPath { get; } = string.Empty;

    /// <summary>The estimated bytes needed for the package.</summary>
    public long RequiredBytes { get; }

    /// <summary>The bytes reported free before the download.</summary>
    public long AvailableBytes { get; }
}
