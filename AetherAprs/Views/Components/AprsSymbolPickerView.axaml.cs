// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.ViewModels.Components;
using AetherAprs.Models.Aprs;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using DialogHostAvalonia;
using SkiaSharp;
using System;
using System.IO;
using System.Threading.Tasks;

namespace AetherAprs.Views.Components;

public partial class AprsSymbolPickerView : UserControl
{
    public static readonly StyledProperty<string> TableCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string>(nameof(TableCharacter), "/");

    public static readonly StyledProperty<string> CodeCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string>(nameof(CodeCharacter), "[");

    public static readonly StyledProperty<string?> OverlayCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string?>(nameof(OverlayCharacter));

    public static readonly StyledProperty<Bitmap?> BaseSymbolPreviewProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, Bitmap?>(nameof(BaseSymbolPreview));

    public static readonly StyledProperty<Bitmap?> OverlayPreviewProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, Bitmap?>(nameof(OverlayPreview));

    public static readonly StyledProperty<Bitmap?> FinalSymbolPreviewProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, Bitmap?>(nameof(FinalSymbolPreview));

    public static readonly StyledProperty<bool> IsOverlayEnabledProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, bool>(nameof(IsOverlayEnabled));

    public static readonly StyledProperty<SymbolSelectorViewModel?> SymbolSelectorViewModelProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, SymbolSelectorViewModel?>(nameof(SymbolSelectorViewModel));

    public static readonly StyledProperty<SymbolSelectorViewModel?> OverlaySelectorViewModelProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, SymbolSelectorViewModel?>(nameof(OverlaySelectorViewModel));

    private readonly IAprsSymbolBitmapProvider? _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;

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

    public string? OverlayCharacter
    {
        get => GetValue(OverlayCharacterProperty);
        set => SetValue(OverlayCharacterProperty, value);
    }

    public Bitmap? BaseSymbolPreview
    {
        get => GetValue(BaseSymbolPreviewProperty);
        private set => SetValue(BaseSymbolPreviewProperty, value);
    }

    public Bitmap? OverlayPreview
    {
        get => GetValue(OverlayPreviewProperty);
        private set => SetValue(OverlayPreviewProperty, value);
    }

    public Bitmap? FinalSymbolPreview
    {
        get => GetValue(FinalSymbolPreviewProperty);
        private set => SetValue(FinalSymbolPreviewProperty, value);
    }

    public bool IsOverlayEnabled
    {
        get => GetValue(IsOverlayEnabledProperty);
        private set => SetValue(IsOverlayEnabledProperty, value);
    }

    public SymbolSelectorViewModel? SymbolSelectorViewModel
    {
        get => GetValue(SymbolSelectorViewModelProperty);
        private set => SetValue(SymbolSelectorViewModelProperty, value);
    }

    public SymbolSelectorViewModel? OverlaySelectorViewModel
    {
        get => GetValue(OverlaySelectorViewModelProperty);
        private set => SetValue(OverlaySelectorViewModelProperty, value);
    }

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
        UpdatePreviews();
    }

    private void OnPickerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == TableCharacterProperty ||
            change.Property == CodeCharacterProperty ||
            change.Property == OverlayCharacterProperty)
        {
            UpdatePreviews();
        }
    }

    private void UpdatePreviews()
    {
        if (_symbolBitmapProvider is null || TableCharacter.Length != 1 || CodeCharacter.Length != 1)
        {
            BaseSymbolPreview = null;
            FinalSymbolPreview = null;
            OverlayPreview = null;
            IsOverlayEnabled = false;
            return;
        }

        try
        {
            var table = TableCharacter[0].ToSymbolTable();
            var code = CodeCharacter[0].ToSymbolCode();

            // Update base symbol preview (without overlay)
            var baseSymbol = new Symbol(table, code);
            BaseSymbolPreview = _previewFactory(_symbolBitmapProvider.GetSymbolBitmap(baseSymbol));

            // Enable overlay only for Alternate table
            IsOverlayEnabled = table == SymbolTable.Alternate;

            // Update overlay preview if applicable
            if (IsOverlayEnabled && !string.IsNullOrEmpty(OverlayCharacter) && OverlayCharacter.Length == 1)
            {
                try
                {
                    var overlayCode = OverlayCharacter[0].ToSymbolCode();
                    OverlayPreview = _previewFactory(_symbolBitmapProvider.GetOverlayBitmap(overlayCode));

                    // Create final symbol with overlay
                    var finalSymbol = new Symbol(table, code, OverlayCharacter[0]);
                    FinalSymbolPreview = _previewFactory(_symbolBitmapProvider.GetSymbolBitmap(finalSymbol));
                }
                catch
                {
                    OverlayPreview = null;
                    FinalSymbolPreview = BaseSymbolPreview;
                }
            }
            else
            {
                OverlayPreview = null;
                FinalSymbolPreview = BaseSymbolPreview;
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            BaseSymbolPreview = null;
            FinalSymbolPreview = null;
            OverlayPreview = null;
            IsOverlayEnabled = false;
        }
    }

    private async void OnBaseSymbolButtonClick(object? sender, RoutedEventArgs e)
    {
        var table = TableCharacter.Length == 1 ? TableCharacter[0].ToSymbolTable() : SymbolTable.Primary;
        var code = CodeCharacter.Length == 1 ? CodeCharacter[0].ToSymbolCode() : SymbolCode.LeftSquareBracket;

        SymbolSelectorViewModel = new SymbolSelectorViewModel(table, code);

        var view = new SymbolSelectorView { DataContext = SymbolSelectorViewModel };

        await DialogHost.Show(view, "RootDialogHost");

        if (SymbolSelectorViewModel.IsSelected && SymbolSelectorViewModel.SelectedSymbolCode.HasValue)
        {
            TableCharacter = SymbolSelectorViewModel.SelectedTable.ToChar().ToString();
            CodeCharacter = SymbolSelectorViewModel.SelectedSymbolCode.Value.ToChar().ToString();

            // Clear overlay if switching to Primary table
            if (SymbolSelectorViewModel.SelectedTable == SymbolTable.Primary)
            {
                OverlayCharacter = null;
            }
        }
    }

    private async void OnOverlayButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!IsOverlayEnabled)
        {
            return;
        }

        // For overlay selection, we use the overlay sprite sheet, but show it like symbol selection
        var currentCode = !string.IsNullOrEmpty(OverlayCharacter) && OverlayCharacter.Length == 1
            ? OverlayCharacter[0].ToSymbolCode()
            : SymbolCode.Digit0;

        // We'll use Alternate table for overlay selection since overlay characters are typically 0-9, A-Z
        OverlaySelectorViewModel = new SymbolSelectorViewModel(SymbolTable.Alternate, currentCode, isOverlayMode: true);

        var view = new SymbolSelectorView { DataContext = OverlaySelectorViewModel };

        await DialogHost.Show(view, "RootDialogHost");

        if (OverlaySelectorViewModel.IsSelected && OverlaySelectorViewModel.SelectedSymbolCode.HasValue)
        {
            OverlayCharacter = OverlaySelectorViewModel.SelectedSymbolCode.Value.ToChar().ToString();
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
