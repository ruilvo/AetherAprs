// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace AetherAprs.Imaging;

/// <summary>
/// Provides <see cref="SKBitmap"/> images for APRS symbols by extracting and
/// compositing from sprite sheets.
/// </summary>
public sealed class AprsSymbolBitmapProvider : IDisposable
{
    /// <summary>Number of columns in each sprite sheet.</summary>
    private const int Columns = 16;

    /// <summary>Number of rows in each sprite sheet.</summary>
    private const int Rows = 6;

    /// <summary>Size of each cell in pixels (2x resolution).</summary>
    private const int CellSize = 128;

    /// <summary>ASCII code offset (first printable character).</summary>
    private const int CodeOffset = 0x20;

    private static readonly Uri[] SpriteSheetUris =
    [
        // Primary sheet
        new("avares://AetherAprs/Assets/Aprs/aprs-symbols-64-0_2x.png"),
        // Secondary sheet
        new("avares://AetherAprs/Assets/Aprs/aprs-symbols-64-1_2x.png"),
        // Overlay sheet
        new("avares://AetherAprs/Assets/Aprs/aprs-symbols-64-2_2x.png"),
    ];

    private readonly SKBitmap[] _spriteSheets;
    private readonly Dictionary<(int table, int code), SKBitmap> _symbolCache = new();
    private readonly Dictionary<(int table, int code, char? overlay), SKBitmap> _compositeCache = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AprsSymbolBitmapProvider"/>,
    /// loading all three sprite sheet bitmaps from assembly resources.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if any sprite sheet fails to load.</exception>
    public AprsSymbolBitmapProvider()
    {
        _spriteSheets = new SKBitmap[3];

        for (var i = 0; i < 3; i++)
        {
            using var stream = AssetLoader.Open(SpriteSheetUris[i]);

            if (stream == null)
            {
                throw new InvalidOperationException(
                    $"Failed to open sprite sheet resource: {SpriteSheetUris[i]}");
            }

            var bitmap = SKBitmap.Decode(stream);

            if (bitmap == null)
            {
                throw new InvalidOperationException(
                    $"Failed to decode sprite sheet: {SpriteSheetUris[i]}");
            }

            _spriteSheets[i] = bitmap;
        }
    }

    /// <summary>
    /// Gets the bitmap for an APRS symbol, with optional overlay composited on top.
    /// </summary>
    /// <param name="symbol">The APRS symbol to render.</param>
    /// <returns>A new (or cached) <see cref="SKBitmap"/> for the given symbol.</returns>
    public SKBitmap GetSymbolBitmap(Symbol symbol)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tableIndex = (int)symbol.Table;
        var codeValue = (byte)symbol.Code;

        if (!symbol.Overlay.HasValue)
        {
            return GetOrCreateSymbolBitmap(tableIndex, codeValue);
        }

        var key = (tableIndex, codeValue, symbol.Overlay);
        if (_compositeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var composite = CreateCompositeBitmap(tableIndex, codeValue, symbol.Overlay.Value);
        _compositeCache[key] = composite;
        return composite;
    }

    /// <summary>
    /// Gets the overlay-only bitmap for a given overlay character.
    /// </summary>
    /// <param name="overlayChar">The overlay character.</param>
    /// <returns>A cached <see cref="SKBitmap"/> of the overlay glyph.</returns>
    public SKBitmap GetOverlayBitmap(char overlayChar)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var codeValue = (int)overlayChar;
        return GetOrCreateSymbolBitmap(tableIndex: 2, codeValue);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var bitmap in _symbolCache.Values)
        {
            bitmap.Dispose();
        }

        _symbolCache.Clear();

        foreach (var bitmap in _compositeCache.Values)
        {
            bitmap.Dispose();
        }

        _compositeCache.Clear();

        foreach (var sheet in _spriteSheets)
        {
            sheet.Dispose();
        }
    }

    private static (int row, int col) GetCellPosition(int codeValue)
    {
        var index = codeValue - CodeOffset;
        return (index / Columns, index % Columns);
    }

    private static SKBitmap ExtractSubBitmap(SKBitmap source, int col, int row)
    {
        var x = col * CellSize;
        var y = row * CellSize;

        var subBitmap = new SKBitmap(CellSize, CellSize);
        using var canvas = new SKCanvas(subBitmap);
        canvas.DrawBitmap(
            source,
            new SKRect(x, y, x + CellSize, y + CellSize),
            new SKRect(0, 0, CellSize, CellSize));
        return subBitmap;
    }

    private static SKBitmap DeepCopy(SKBitmap source)
    {
        var copy = new SKBitmap(source.Width, source.Height);
        using var canvas = new SKCanvas(copy);
        canvas.DrawBitmap(source, 0, 0);
        return copy;
    }

    private SKBitmap GetOrCreateSymbolBitmap(int tableIndex, int codeValue)
    {
        var key = (tableIndex, codeValue);

        if (_symbolCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var (row, col) = GetCellPosition(codeValue);
        var bitmap = ExtractSubBitmap(_spriteSheets[tableIndex], col, row);
        _symbolCache[key] = bitmap;
        return bitmap;
    }

    private SKBitmap CreateCompositeBitmap(int tableIndex, int codeValue, char overlayChar)
    {
        var baseSymbol = GetOrCreateSymbolBitmap(tableIndex, codeValue);
        var result = DeepCopy(baseSymbol);

        var (overlayRow, overlayCol) = GetCellPosition(overlayChar);
        var overlayBitmap = ExtractSubBitmap(_spriteSheets[2], overlayCol, overlayRow);

        // Composite the overlay glyph onto the top-left corner of the base symbol
        using var canvas = new SKCanvas(result);
        var overlayRect = new SKRect(0, 0, CellSize / 2, CellSize / 2);
        canvas.DrawBitmap(overlayBitmap, overlayRect);
        canvas.Flush();

        overlayBitmap.Dispose();

        return result;
    }
}
