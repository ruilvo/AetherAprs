// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using System.IO;

namespace AetherAprs.ViewModels.Components;

public partial class SymbolSelectorViewModel : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider? _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial SymbolCode? SelectedSymbolCode { get; set; }

    [ObservableProperty]
    public partial SymbolTable SelectedTable { get; set; } = SymbolTable.Primary;

    public bool IsSelected { get; private set; }

    public IReadOnlyList<SymbolGridItem> PrimarySymbols { get; }
    public IReadOnlyList<SymbolGridItem> AlternateSymbols { get; }

    public SymbolSelectorViewModel(SymbolTable currentTable, SymbolCode currentCode, bool isOverlayMode = false)
    {
        _previewFactory = CreatePreviewBitmap;

        try
        {
            _symbolBitmapProvider = App.GetService<IAprsSymbolBitmapProvider>();
        }
        catch (InvalidOperationException)
        {
            _symbolBitmapProvider = null;
        }

        SelectedTable = currentTable;
        SelectedSymbolCode = currentCode;
        SelectedTabIndex = currentTable == SymbolTable.Primary ? 0 : 1;

        // Generate all symbol codes (printable ASCII from ! to ~)
        var allCodes = Enumerable.Range(0x21, 0x7E - 0x21 + 1)
            .Select(i => (SymbolCode)(byte)i)
            .ToList();

        PrimarySymbols = GenerateSymbolGridItems(SymbolTable.Primary, allCodes);
        AlternateSymbols = GenerateSymbolGridItems(SymbolTable.Alternate, allCodes);
    }

    private IReadOnlyList<SymbolGridItem> GenerateSymbolGridItems(SymbolTable table, IEnumerable<SymbolCode> codes)
    {
        return codes.Select(code =>
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
        }).ToList();
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
