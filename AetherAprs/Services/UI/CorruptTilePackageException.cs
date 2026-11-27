// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

namespace AetherAprs.Services.UI;

/// <summary>
/// The kind of defect found in an MBTiles package.
/// </summary>
public enum TilePackageFault
{
    /// <summary>The file could not be opened as a SQLite database.</summary>
    Unreadable,

    /// <summary>The database does not contain the <c>tiles</c> or <c>metadata</c> table.</summary>
    InvalidSchema,

    /// <summary>A required metadata entry is absent.</summary>
    MissingMetadata,

    /// <summary>A metadata entry exists but does not have the expected value.</summary>
    InvalidMetadata,

    /// <summary>The <c>bounds</c> metadata is not a sane WGS84 extent.</summary>
    InvalidBounds
}

/// <summary>
/// Thrown when an MBTiles file exists but cannot be used because it is not a valid
/// package. A missing file is reported with <see cref="System.IO.FileNotFoundException"/>
/// instead, so callers can tell the two conditions apart.
/// </summary>
public sealed class CorruptTilePackageException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CorruptTilePackageException"/> class.</summary>
    public CorruptTilePackageException()
    {
    }

    /// <summary>Initializes a new instance with a message.</summary>
    /// <param name="message">The failure description.</param>
    public CorruptTilePackageException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and an inner failure.</summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The failure that revealed the corrupt package.</param>
    public CorruptTilePackageException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new instance with the package path and the specific defect.</summary>
    /// <param name="filePath">The package that failed validation.</param>
    /// <param name="fault">The kind of defect that was found.</param>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The failure that revealed the corrupt package, if any.</param>
    public CorruptTilePackageException(
        string filePath,
        TilePackageFault fault,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        FilePath = filePath;
        Fault = fault;
    }

    /// <summary>The path of the package that failed validation, or an empty string.</summary>
    public string FilePath { get; } = string.Empty;

    /// <summary>The kind of defect that was found.</summary>
    public TilePackageFault Fault { get; }
}
