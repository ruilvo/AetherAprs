// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AetherAprs.ViewModels.Components;

public partial class SymbolSelectorViewModel : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider? _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;
    private readonly bool _isOverlayMode;

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial SymbolCode? SelectedSymbolCode { get; set; }

    [ObservableProperty]
    public partial SymbolTable SelectedTable { get; set; } = SymbolTable.Primary;

    public bool IsSelected { get; private set; }

    public event EventHandler? SymbolSelected;

    public bool IsOverlayMode => _isOverlayMode;

    public IReadOnlyList<SymbolGridItem> PrimarySymbols { get; }
    public IReadOnlyList<SymbolGridItem> AlternateSymbols { get; }

    public SymbolSelectorViewModel(
        IAprsSymbolBitmapProvider? symbolBitmapProvider,
        SymbolTable currentTable, 
        SymbolCode currentCode, 
        bool isOverlayMode = false)
    {
        _symbolBitmapProvider = symbolBitmapProvider;
        _previewFactory = CreatePreviewBitmap;
        _isOverlayMode = isOverlayMode;

        SelectedTable = currentTable;
        SelectedSymbolCode = currentCode;
        SelectedTabIndex = currentTable == SymbolTable.Primary ? 0 : 1;

        // Generate all symbol codes (printable ASCII from ! to ~)
        var allCodes = Enumerable.Range(0x21, 0x7E - 0x21 + 1)
            .Select(i => (SymbolCode)(byte)i)
            .ToList();

        if (_isOverlayMode)
        {
            // In overlay mode, show only overlay glyphs (no base symbols)
            PrimarySymbols = GenerateOverlayGridItems(allCodes);
            AlternateSymbols = Array.Empty<SymbolGridItem>();
        }
        else
        {
            PrimarySymbols = GenerateSymbolGridItems(SymbolTable.Primary, allCodes);
            AlternateSymbols = GenerateSymbolGridItems(SymbolTable.Alternate, allCodes);
        }
    }

    private IReadOnlyList<SymbolGridItem> GenerateSymbolGridItems(SymbolTable table, IEnumerable<SymbolCode> codes)
    {
        return [.. codes.Select(code =>
        {
            Bitmap? preview = null;
            if (_symbolBitmapProvider != null)
            {
                try
                {
                    var symbol = new Symbol(table, code);
                    preview = _previewFactory(_symbolBitmapProvider.GetSymbolBitmap(symbol));
                }
                catch
                {
                    preview = null;
                }
            }

            return new SymbolGridItem
            {
                Code = code,
                Table = table,
                Preview = preview,
                Character = code.ToChar().ToString()
            };
        })];
    }

    private IReadOnlyList<SymbolGridItem> GenerateOverlayGridItems(IEnumerable<SymbolCode> codes)
    {
        return [.. codes.Select(code =>
        {
            Bitmap? preview = null;
            if (_symbolBitmapProvider != null)
            {
                try
                {
                    preview = _previewFactory(_symbolBitmapProvider.GetOverlayBitmap(code));
                }
                catch
                {
                    preview = null;
                }
            }

            return new SymbolGridItem
            {
                Code = code,
                Table = SymbolTable.Alternate, // Overlays are always associated with Alternate table
                Preview = preview,
                Character = code.ToChar().ToString()
            };
        })];
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        SelectedTable = value == 0 ? SymbolTable.Primary : SymbolTable.Alternate;
    }

    [RelayCommand]
    private void SelectPrimaryTab()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    private void SelectAlternateTab()
    {
        SelectedTabIndex = 1;
    }

    [RelayCommand]
    private void SelectSymbol(SymbolGridItem item)
    {
        SelectedSymbolCode = item.Code;
        SelectedTable = item.Table;
        IsSelected = true;
        SymbolSelected?.Invoke(this, EventArgs.Empty);
    }

    private static Bitmap CreatePreviewBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}

public class SymbolGridItem
{
    public SymbolCode Code { get; init; }
    public SymbolTable Table { get; init; }
    public Bitmap? Preview { get; init; }
    public string Character { get; init; } = string.Empty;
}
