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
public partial class AprsSymbolPickerViewModel(IAprsSymbolBitmapProvider symbolBitmapProvider, ILogger<AprsSymbolPickerViewModel>? logger = null) : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider = symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider));
    private readonly ILogger<AprsSymbolPickerViewModel>? _logger = logger;
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
    public void BeginSuppressNotifications() => _suppressPropertyNotifications = true;

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
            SymbolTable table = TableCharacter[0].ToSymbolTable();
            SymbolCode code = CodeCharacter[0].ToSymbolCode();

            // Update base symbol preview (without overlay)
            Symbol baseSymbol = new(table, code);
            SKBitmap baseBitmap = _symbolBitmapProvider.GetSymbolBitmap(baseSymbol);
            BaseSymbolPreview = baseBitmap != null ? CreatePreviewBitmap(baseBitmap) : null;

            // Enable overlay only for Alternate table
            IsOverlayEnabled = table == SymbolTable.Alternate;

            // Update overlay preview if applicable
            if (IsOverlayEnabled && !string.IsNullOrEmpty(OverlayCharacter) && OverlayCharacter.Length == 1)
            {
                try
                {
                    SymbolCode overlayCode = OverlayCharacter[0].ToSymbolCode();
                    SKBitmap overlayBitmap = _symbolBitmapProvider.GetOverlayBitmap(overlayCode);
                    OverlayPreview = overlayBitmap != null ? CreatePreviewBitmap(overlayBitmap) : null;

                    // Create final symbol with overlay
                    Symbol finalSymbol = new(table, code, OverlayCharacter[0]);
                    SKBitmap finalBitmap = _symbolBitmapProvider.GetSymbolBitmap(finalSymbol);
                    FinalSymbolPreview = finalBitmap != null ? CreatePreviewBitmap(finalBitmap) : null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to create overlay preview");
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
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update symbol previews");
            BaseSymbolPreview = null;
            FinalSymbolPreview = null;
            OverlayPreview = null;
            IsOverlayEnabled = false;
        }
    }

    [RelayCommand]
    private void OpenBaseSymbolSelector()
    {
        SymbolTable table = TableCharacter.Length == 1 ? TableCharacter[0].ToSymbolTable() : SymbolTable.Primary;
        SymbolCode code = CodeCharacter.Length == 1 ? CodeCharacter[0].ToSymbolCode() : SymbolCode.LeftSquareBracket;

        // Documented exception to the child-ViewModel-injection rule: SymbolSelectorViewModel is a
        // transient per-dialog selector whose arguments are derived at invocation time; see AGENTS.md
        // "Child ViewModel Injection".
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

        SymbolCode currentCode = !string.IsNullOrEmpty(OverlayCharacter) && OverlayCharacter.Length == 1
            ? OverlayCharacter[0].ToSymbolCode()
            : SymbolCode.Digit0;

        // Documented exception to the child-ViewModel-injection rule (transient per-dialog selector) — see AGENTS.md.
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

    private static Bitmap? CreatePreviewBitmap(SKBitmap? bitmap)
    {
        if (bitmap == null)
        {
            return null;
        }

        try
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using MemoryStream stream = new(data.ToArray());
            return new Bitmap(stream);
        }
        catch (InvalidOperationException)
        {
            // Avalonia platform not initialized (e.g., in tests)
            return null;
        }
    }

    /// <summary>
    /// Reports dialog errors from the View to the ViewModel for logging.
    /// Called by AprsSymbolPickerComponent when dialog operations fail.
    /// </summary>
    public void ReportDialogError(string operation, Exception exception) => _logger?.LogError(exception, "Failed to {Operation} in symbol picker", operation);
}
