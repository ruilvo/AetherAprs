// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models.Aprs;
using Mapsui.Styles;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace AetherAprs.Imaging;

/// <summary>
/// Converts <see cref="Symbol"/> values into MapsUI-compatible <see cref="ImageStyle"/>
/// instances by extracting sub-bitmaps from sprite sheets, compositing overlays, and
/// encoding the result as a base64 PNG URI for MapsUI's image pipeline.
/// </summary>
public sealed class AprsSymbolMapConverter : IDisposable
{
    private readonly AprsSymbolBitmapProvider _provider;
    private readonly Dictionary<(int table, int code, char? overlay), string> _base64Cache = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of <see cref="AprsSymbolMapConverter"/>.
    /// </summary>
    /// <param name="provider">The bitmap provider that loads and composites sprite sheets.</param>
    public AprsSymbolMapConverter(AprsSymbolBitmapProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Creates an <see cref="ImageStyle"/> for the given APRS symbol at the specified scale.
    /// The symbol bitmap is composited (including any overlay), encoded as a base64 PNG,
    /// and referenced via MapsUI's <c>base64-content://</c> URI scheme.
    /// </summary>
    /// <param name="symbol">The APRS symbol to render.</param>
    /// <param name="scale">Scale factor (1.0 = 128px at native 2x resolution).</param>
    /// <returns>A configured <see cref="ImageStyle"/> ready to use on a MapsUI feature.</returns>
    public ImageStyle CreateImageStyle(Symbol symbol, double scale = 0.5)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = ((int)symbol.Table, (byte)symbol.Code, symbol.Overlay);

        if (!_base64Cache.TryGetValue(key, out var base64Uri))
        {
            var bitmap = _provider.GetSymbolBitmap(symbol);
            base64Uri = EncodeToBase64Uri(bitmap);
            _base64Cache[key] = base64Uri;
        }

        return new ImageStyle
        {
            Image = base64Uri,
            SymbolScale = scale,
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _base64Cache.Clear();
        _provider.Dispose();
    }

    private static string EncodeToBase64Uri(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        return "base64-content://" + Convert.ToBase64String(bytes);
    }
}