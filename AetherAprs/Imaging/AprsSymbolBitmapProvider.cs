// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Avalonia.Platform;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace AetherAprs.Imaging;

/// <summary>
/// Provides <see cref="SKBitmap"/> images for APRS symbols by extracting and
/// compositing from sprite sheets.
/// </summary>
public interface IAprsSymbolBitmapProvider : IDisposable
{
    SKBitmap GetSymbolBitmap(Symbol symbol);
    SKBitmap GetOverlayBitmap(SymbolCode overlayChar);
}

public sealed class AprsSymbolBitmapProvider : IAprsSymbolBitmapProvider
{
    /// <summary>Number of columns in each sprite sheet.</summary>
    private const int Columns = 16;

    /// <summary>Number of rows in each sprite sheet.</summary>
    private const int Rows = 6;

    /// <summary>Size of each cell in pixels (2x resolution).</summary>
    private const int CellSize = 128;

    /// <summary>
    /// ASCII code offset for the packed symbol sheets. The sheets omit the
    /// space character, so the first cell represents '!'.
    /// </summary>
    private const int CodeOffset = 0x21;

    private enum SpriteSheet
    {
        Primary = 0,
        Secondary = 1,
        Overlay = 2
    }

    private static readonly Dictionary<SpriteSheet, Uri> SpriteSheetUris = new()
    {
        [SpriteSheet.Primary] = new("avares://AetherAprs/Assets/Aprs/aprs-symbols-64-0_2x.png"),
        [SpriteSheet.Secondary] = new("avares://AetherAprs/Assets/Aprs/aprs-symbols-64-1_2x.png"),
        [SpriteSheet.Overlay] = new("avares://AetherAprs/Assets/Aprs/aprs-symbols-64-2_2x.png"),
    };

    private record struct SpriteKey(SpriteSheet Sheet, SymbolCode Code);

    private readonly Dictionary<SpriteSheet, SKBitmap> _spriteSheets;
    private readonly Dictionary<SpriteKey, SKBitmap> _spriteCache = [];
    private readonly Dictionary<Symbol, SKBitmap> _symbolCache = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AprsSymbolBitmapProvider"/>,
    /// loading all three sprite sheet bitmaps from assembly resources.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if any sprite sheet fails to load.</exception>
    public AprsSymbolBitmapProvider()
    {
        _spriteSheets = [];

        foreach ((SpriteSheet sheet, Uri? uri) in SpriteSheetUris)
        {
            using Stream stream = AssetLoader.Open(uri) ?? throw new InvalidOperationException(
                    $"Failed to open sprite sheet resource: {SpriteSheetUris[sheet]}");

            SKBitmap bitmap = SKBitmap.Decode(stream) ?? throw new InvalidOperationException(
                    $"Failed to decode sprite sheet: {SpriteSheetUris[sheet]}");

            _spriteSheets[sheet] = bitmap;
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

        // If there is no overlay, we can return the base symbol bitmap directly
        // from the sprite sheet.
        if (!symbol.Overlay.HasValue)
        {
            SpriteKey skey = new() { Sheet = (SpriteSheet)symbol.Table, Code = symbol.Code };
            return GetOrCreateSymbolBitmap(skey);
        }

        if (_symbolCache.TryGetValue(symbol, out SKBitmap? cached))
        {
            return cached;
        }

        SKBitmap composite = CreateCompositeBitmap(symbol);
        _symbolCache[symbol] = composite;
        return composite;
    }

    /// <summary>
    /// Gets the overlay-only bitmap for a given overlay character.
    /// </summary>
    /// <param name="overlayChar">The overlay character.</param>
    /// <returns>A cached <see cref="SKBitmap"/> of the overlay glyph.</returns>
    public SKBitmap GetOverlayBitmap(SymbolCode overlayChar)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        SpriteKey skey = new() { Sheet = SpriteSheet.Overlay, Code = overlayChar };
        return GetOrCreateSymbolBitmap(skey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (SKBitmap bitmap in _spriteCache.Values)
        {
            bitmap.Dispose();
        }
        _spriteCache.Clear();

        foreach (SKBitmap bitmap in _symbolCache.Values)
        {
            bitmap.Dispose();
        }
        _symbolCache.Clear();

        foreach (SKBitmap sheet in _spriteSheets.Values)
        {
            sheet.Dispose();
        }
        _spriteSheets.Clear();
    }

    internal static (int row, int col) GetCellPosition(SymbolCode codeValue)
    {
        if ((int)codeValue < CodeOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(codeValue), "The symbol sprite sheets do not contain the space character.");
        }

        int index = (int)codeValue - CodeOffset;
        return (index / Columns, index % Columns);
    }

    private static SKBitmap ExtractSubBitmap(SKBitmap source, int col, int row)
    {
        int x = col * CellSize;
        int y = row * CellSize;

        SKBitmap subBitmap = new(CellSize, CellSize);
        using SKCanvas canvas = new(subBitmap);
        canvas.DrawBitmap(
            source,
            new SKRect(x, y, x + CellSize, y + CellSize),
            new SKRect(0, 0, CellSize, CellSize));
        return subBitmap;
    }

    private static SKBitmap DeepCopy(SKBitmap source)
    {
        SKBitmap copy = new(source.Width, source.Height);
        using SKCanvas canvas = new(copy);
        canvas.DrawBitmap(source, 0, 0);
        return copy;
    }

    private SKBitmap GetOrCreateSymbolBitmap(SpriteKey skey)
    {
        if (_spriteCache.TryGetValue(skey, out SKBitmap? cached))
        {
            return cached;
        }

        (int row, int col) = GetCellPosition(skey.Code);
        SKBitmap bitmap = ExtractSubBitmap(_spriteSheets[skey.Sheet], col, row);
        _spriteCache[skey] = bitmap;
        return bitmap;
    }

    private SKBitmap CreateCompositeBitmap(Symbol symbol)
    {
        SpriteKey skey = new() { Sheet = (SpriteSheet)symbol.Table, Code = symbol.Code };
        SKBitmap baseSymbol = GetOrCreateSymbolBitmap(skey);
        SKBitmap result = DeepCopy(baseSymbol);

        SymbolCode overlayCode = symbol.Overlay!.Value.ToSymbolCode();
        (int overlayRow, int overlayCol) = GetCellPosition(overlayCode);
        SKBitmap overlayBitmap = ExtractSubBitmap(_spriteSheets[SpriteSheet.Overlay], overlayCol, overlayRow);

        // Composite the overlay glyph centered at half size over the base symbol
        using SKCanvas canvas = new(result);
        int overlaySize = CellSize / 2;
        int offset = (CellSize - overlaySize) / 2;
        SKRect overlayRect = new(offset, offset, offset + overlaySize, offset + overlaySize);
        canvas.DrawBitmap(overlayBitmap, overlayRect);
        canvas.Flush();

        overlayBitmap.Dispose();

        return result;
    }
}
