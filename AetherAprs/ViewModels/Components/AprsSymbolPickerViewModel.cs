// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System;
using System.IO;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// ViewModel for the APRS symbol picker component.
/// Manages symbol selection, preview generation, and overlay logic.
/// </summary>
public partial class AprsSymbolPickerViewModel : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider;
    private readonly ILogger<AprsSymbolPickerViewModel>? _logger;
    private bool _suppressPropertyNotifications;

    [ObservableProperty]
    public partial string TableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string CodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial string? OverlayCharacter { get; set; }

    [ObservableProperty]
    public partial Bitmap? BaseSymbolPreview { get; set; }

    [ObservableProperty]
    public partial Bitmap? OverlayPreview { get; set; }

    [ObservableProperty]
    public partial Bitmap? FinalSymbolPreview { get; set; }

    [ObservableProperty]
    public partial bool IsOverlayEnabled { get; set; }

    [ObservableProperty]
    public partial SymbolSelectorViewModel? SymbolSelectorViewModel { get; set; }

    [ObservableProperty]
    public partial SymbolSelectorViewModel? OverlaySelectorViewModel { get; set; }

    public event EventHandler? OpenSymbolSelectorRequested;
    public event EventHandler? OpenOverlaySelectorRequested;

    public AprsSymbolPickerViewModel(IAprsSymbolBitmapProvider symbolBitmapProvider, ILogger<AprsSymbolPickerViewModel>? logger = null)
    {
        _symbolBitmapProvider = symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider));
        _logger = logger;
        // Don't call UpdatePreviews() in constructor - let property setters handle it
        // This avoids Avalonia platform dependencies during construction (important for tests and design-time)
    }

    partial void OnTableCharacterChanged(string value)
    {
        // Only update previews if we have valid character (avoid updates during initialization)
        if (value.Length == 1 && CodeCharacter.Length == 1)
        {
            UpdatePreviews();
        }
    }

    partial void OnCodeCharacterChanged(string value)
    {
        // Only update previews if we have valid character (avoid updates during initialization)
        if (value.Length == 1 && TableCharacter.Length == 1)
        {
            UpdatePreviews();
        }
    }

    partial void OnOverlayCharacterChanged(string? value)
    {
        // Only update previews if base symbols are valid
        if (TableCharacter.Length == 1 && CodeCharacter.Length == 1)
        {
            UpdatePreviews();
        }
    }

    /// <summary>
    /// Temporarily suppress property change notifications during bulk updates.
    /// Used when initializing from saved settings to avoid triggering unwanted events.
    /// </summary>
    public void BeginSuppressNotifications()
    {
        _suppressPropertyNotifications = true;
    }

    /// <summary>
    /// Resume property change notifications and trigger preview update.
    /// </summary>
    public void EndSuppressNotifications()
    {
        _suppressPropertyNotifications = false;
        // Trigger preview update now that all properties are set
        if (TableCharacter.Length == 1 && CodeCharacter.Length == 1)
        {
            UpdatePreviews();
        }
    }

    private void UpdatePreviews()
    {
        if (_suppressPropertyNotifications)
        {
            return;
        }

        if (TableCharacter.Length != 1 || CodeCharacter.Length != 1)
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
            BaseSymbolPreview = CreatePreviewBitmap(_symbolBitmapProvider.GetSymbolBitmap(baseSymbol));

            // Enable overlay only for Alternate table
            IsOverlayEnabled = table == SymbolTable.Alternate;

            // Update overlay preview if applicable
            if (IsOverlayEnabled && !string.IsNullOrEmpty(OverlayCharacter) && OverlayCharacter.Length == 1)
            {
                try
                {
                    var overlayCode = OverlayCharacter[0].ToSymbolCode();
                    OverlayPreview = CreatePreviewBitmap(_symbolBitmapProvider.GetOverlayBitmap(overlayCode));

                    // Create final symbol with overlay
                    var finalSymbol = new Symbol(table, code, OverlayCharacter[0]);
                    FinalSymbolPreview = CreatePreviewBitmap(_symbolBitmapProvider.GetSymbolBitmap(finalSymbol));
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

    [RelayCommand]
    private void OpenBaseSymbolSelector()
    {
        var table = TableCharacter.Length == 1 ? TableCharacter[0].ToSymbolTable() : SymbolTable.Primary;
        var code = CodeCharacter.Length == 1 ? CodeCharacter[0].ToSymbolCode() : SymbolCode.LeftSquareBracket;

        SymbolSelectorViewModel = new SymbolSelectorViewModel(_symbolBitmapProvider, table, code);
        OpenSymbolSelectorRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenOverlaySelector()
    {
        if (!IsOverlayEnabled)
        {
            return;
        }

        var currentCode = !string.IsNullOrEmpty(OverlayCharacter) && OverlayCharacter.Length == 1
            ? OverlayCharacter[0].ToSymbolCode()
            : SymbolCode.Digit0;

        OverlaySelectorViewModel = new SymbolSelectorViewModel(_symbolBitmapProvider, SymbolTable.Alternate, currentCode, isOverlayMode: true);
        OpenOverlaySelectorRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyBaseSymbolSelection()
    {
        if (SymbolSelectorViewModel is { IsSelected: true, SelectedSymbolCode: not null })
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

    public void ApplyOverlaySelection()
    {
        if (OverlaySelectorViewModel is { IsSelected: true, SelectedSymbolCode: not null })
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

    /// <summary>
    /// Reports dialog errors from the View to the ViewModel for logging.
    /// Called by AprsSymbolPickerComponent when dialog operations fail.
    /// </summary>
    public void ReportDialogError(string operation, Exception exception)
    {
        _logger?.LogError(exception, "Failed to {Operation} in symbol picker", operation);
    }
}
