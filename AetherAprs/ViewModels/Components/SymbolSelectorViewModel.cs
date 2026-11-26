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
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Components;

public partial class SymbolSelectorViewModel : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider? _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;
    private readonly bool _isOverlayMode;
    private bool _previewsLoading;

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
        List<SymbolCode> allCodes = [.. Enumerable.Range(0x21, 0x7E - 0x21 + 1).Select(i => (SymbolCode)(byte)i)];

        if (_isOverlayMode)
        {
            // In overlay mode, show only overlay glyphs (no base symbols)
            PrimarySymbols = GenerateOverlayGridItems(allCodes);
            AlternateSymbols = [];
        }
        else
        {
            PrimarySymbols = GenerateSymbolGridItems(SymbolTable.Primary, allCodes);
            AlternateSymbols = GenerateSymbolGridItems(SymbolTable.Alternate, allCodes);
        }
    }

    public async Task LoadPreviewsAsync()
    {
        if (_symbolBitmapProvider == null || _previewsLoading)
        {
            return;
        }

        _previewsLoading = true;
        try
        {
            // Let the dialog render before doing the first synchronous bitmap extraction.
            await Task.Yield();

            foreach (SymbolGridItem? item in PrimarySymbols.Concat(AlternateSymbols))
            {
                try
                {
                    Bitmap? preview = _isOverlayMode
                        ? _previewFactory(_symbolBitmapProvider.GetOverlayBitmap(item.Code))
                        : _previewFactory(_symbolBitmapProvider.GetSymbolBitmap(new Symbol(item.Table, item.Code)));
                    item.Preview = preview;
                }
                catch
                {
                    // A missing glyph must not prevent the remaining symbols from loading.
                }

                // Keep the UI responsive and let each generated image appear incrementally.
                await Task.Yield();
            }
        }
        finally
        {
            _previewsLoading = false;
        }
    }

    private static IReadOnlyList<SymbolGridItem> GenerateSymbolGridItems(SymbolTable table, IEnumerable<SymbolCode> codes) => [.. codes.Select(code => new SymbolGridItem
            {
                Code = code,
                Table = table,
                Character = code.ToChar().ToString()
            })];

    private static IReadOnlyList<SymbolGridItem> GenerateOverlayGridItems(IEnumerable<SymbolCode> codes) => [.. codes.Select(code => new SymbolGridItem
            {
                Code = code,
                Table = SymbolTable.Alternate, // Overlays are always associated with Alternate table
                Character = code.ToChar().ToString()
            })];

    partial void OnSelectedTabIndexChanged(int value)
    {
        SelectedTable = value == 0 ? SymbolTable.Primary : SymbolTable.Alternate;
    }

    [RelayCommand]
    private void SelectPrimaryTab() => SelectedTabIndex = 0;

    [RelayCommand]
    private void SelectAlternateTab() => SelectedTabIndex = 1;

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
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(data.ToArray());
        return new Bitmap(stream);
    }
}

public partial class SymbolGridItem : ObservableObject
{
    public SymbolCode Code { get; init; }
    public SymbolTable Table { get; init; }

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    public string Character { get; init; } = string.Empty;
}
