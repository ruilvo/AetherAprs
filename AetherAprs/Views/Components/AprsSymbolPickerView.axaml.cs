// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AetherAprs.Views.Components;

public partial class AprsSymbolPickerView : UserControl
{
    public static readonly StyledProperty<string> TableCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string>(nameof(TableCharacter), "/");

    public static readonly StyledProperty<string> CodeCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string>(nameof(CodeCharacter), "[");

    public static readonly StyledProperty<bool> UseDefaultProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, bool>(nameof(UseDefault));

    public static readonly StyledProperty<bool> ShowInheritOptionProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, bool>(nameof(ShowInheritOption));

    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, bool>(nameof(IsEditable), true);

    public static readonly StyledProperty<Bitmap?> SymbolPreviewProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, Bitmap?>(nameof(SymbolPreview));

    public static readonly StyledProperty<AprsSymbolOption?> SelectedSymbolOptionProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, AprsSymbolOption?>(nameof(SelectedSymbolOption));

    private readonly IAprsSymbolBitmapProvider? _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;
    private bool _updatingSelection;

    public string TableCharacter
    {
        get => GetValue(TableCharacterProperty);
        set => SetValue(TableCharacterProperty, value);
    }

    public string CodeCharacter
    {
        get => GetValue(CodeCharacterProperty);
        set => SetValue(CodeCharacterProperty, value);
    }

    public bool UseDefault
    {
        get => GetValue(UseDefaultProperty);
        set => SetValue(UseDefaultProperty, value);
    }

    public bool ShowInheritOption
    {
        get => GetValue(ShowInheritOptionProperty);
        set => SetValue(ShowInheritOptionProperty, value);
    }

    public bool IsEditable
    {
        get => GetValue(IsEditableProperty);
        private set => SetValue(IsEditableProperty, value);
    }

    public Bitmap? SymbolPreview
    {
        get => GetValue(SymbolPreviewProperty);
        private set => SetValue(SymbolPreviewProperty, value);
    }

    public AprsSymbolOption? SelectedSymbolOption
    {
        get => GetValue(SelectedSymbolOptionProperty);
        set => SetValue(SelectedSymbolOptionProperty, value);
    }

    public IReadOnlyList<AprsSymbolOption> SymbolOptions { get; } =
    [
        new("Human", "/", "["),
        new("Ambulance", "/", "a"),
        new("Car", "/", ">"),
        new("Truck", "/", "k"),
        new("Boat", "/", "s"),
        new("Emergency", "\\", "!"),
        new("Hospital", "\\", "h")
    ];

    public AprsSymbolPickerView()
    {
        InitializeComponent();
        _previewFactory = CreatePreviewBitmap;

        try
        {
            _symbolBitmapProvider = App.GetService<IAprsSymbolBitmapProvider>();
        }
        catch (InvalidOperationException)
        {
            _symbolBitmapProvider = null;
        }

        PropertyChanged += OnPickerPropertyChanged;
        IsEditable = !UseDefault;
        UpdateSelectionAndPreview();
    }

    private void OnPickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == UseDefaultProperty)
        {
            IsEditable = !(bool)change.NewValue!;
            return;
        }

        if (change.Property == SelectedSymbolOptionProperty &&
            !_updatingSelection &&
            change.NewValue is AprsSymbolOption option)
        {
            TableCharacter = option.TableCharacter;
            CodeCharacter = option.CodeCharacter;
            return;
        }

        if (change.Property == TableCharacterProperty ||
            change.Property == CodeCharacterProperty ||
            change.Property == SelectedSymbolOptionProperty)
        {
            UpdateSelectionAndPreview();
        }
    }

    private void UpdateSelectionAndPreview()
    {
        var option = SymbolOptions.FirstOrDefault(item =>
            item.TableCharacter == TableCharacter && item.CodeCharacter == CodeCharacter);

        if (SelectedSymbolOption != option)
        {
            _updatingSelection = true;
            SetCurrentValue(SelectedSymbolOptionProperty, option);
            _updatingSelection = false;
        }

        if (_symbolBitmapProvider is null || TableCharacter.Length != 1 || CodeCharacter.Length != 1)
        {
            SymbolPreview = null;
            return;
        }

        try
        {
            var symbol = new AetherAprs.Models.Aprs.Symbol(
                TableCharacter[0].ToSymbolTable(),
                CodeCharacter[0].ToSymbolCode());
            SymbolPreview = _previewFactory(_symbolBitmapProvider.GetSymbolBitmap(symbol));
        }
        catch (ArgumentOutOfRangeException)
        {
            SymbolPreview = null;
        }
    }

    private static Bitmap CreatePreviewBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
