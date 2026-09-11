// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Configuration;
using AetherAprs.Helpers;
using AetherAprs.Imaging;
using AetherAprs.Models;
using AetherAprs.Models.Aprs;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AetherAprs.ViewModels;

public partial class AddEditPortDialogViewModel : ViewModelBase
{
    private readonly IAprsSymbolBitmapProvider _symbolBitmapProvider;
    private readonly Func<SKBitmap, Bitmap?> _previewFactory;

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

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial PortType SelectedPortType { get; set; } = PortType.AprsIs;

    [ObservableProperty]
    public partial string Server { get; set; } = "euro.aprs2.net";

    [ObservableProperty]
    public partial int ServerPort { get; set; } = 14580;

    [ObservableProperty]
    public partial string Passcode { get; set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "m/50";

    [ObservableProperty]
    public partial int? Ssid { get; set; }

    [ObservableProperty]
    public partial string SymbolTableCharacter { get; set; } = "/";

    [ObservableProperty]
    public partial string SymbolCodeCharacter { get; set; } = "[";

    [ObservableProperty]
    public partial bool UseDefaultSymbol { get; set; } = true;

    [ObservableProperty]
    public partial AprsSymbolOption? SelectedSymbolOption { get; set; }

    [ObservableProperty]
    public partial Bitmap? SymbolPreview { get; private set; }

    [ObservableProperty]
    public partial bool IsSymbolValid { get; private set; }

    [ObservableProperty]
    public partial bool IsRx { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTx { get; set; } = false;

    [ObservableProperty]
    public partial DynamicBeaconMode? SelectedBeaconMode { get; set; }

    [ObservableProperty]
    public partial bool UseDefaultBeaconMode { get; set; } = true;

    public PortType[] PortTypes { get; } = [PortType.AprsIs];

    public DynamicBeaconMode?[] BeaconModes { get; } = [null, DynamicBeaconMode.Walk, DynamicBeaconMode.Drive, DynamicBeaconMode.Custom];

    public AddEditPortDialogViewModel(
        string globalCallsign,
        int nextPortNumber,
        IAprsSymbolBitmapProvider symbolBitmapProvider,
        Func<SKBitmap, Bitmap?>? previewFactory = null,
        string defaultSymbolTableCharacter = "/",
        string defaultSymbolCodeCharacter = "[",
        DynamicBeaconMode defaultBeaconMode = DynamicBeaconMode.Walk)
    {
        _symbolBitmapProvider = symbolBitmapProvider ?? throw new ArgumentNullException(nameof(symbolBitmapProvider));
        _previewFactory = previewFactory ?? CreatePreviewBitmap;
        Name = $"APRS-IS Port {nextPortNumber}";
        Passcode = AprsPasscode.Compute(globalCallsign);
        SymbolTableCharacter = defaultSymbolTableCharacter;
        SymbolCodeCharacter = defaultSymbolCodeCharacter;
        SelectedBeaconMode = defaultBeaconMode;
        UpdateSelectedSymbolOption();
        UpdateSymbolPreview();
    }

    partial void OnSymbolTableCharacterChanged(string value)
    {
        UpdateSelectedSymbolOption();
        UpdateSymbolPreview();
    }

    partial void OnSymbolCodeCharacterChanged(string value)
    {
        UpdateSelectedSymbolOption();
        UpdateSymbolPreview();
    }

    partial void OnSelectedSymbolOptionChanged(AprsSymbolOption? value)
    {
        if (value is null)
        {
            return;
        }

        SymbolTableCharacter = value.TableCharacter;
        SymbolCodeCharacter = value.CodeCharacter;
    }

    [RelayCommand]
    private void SelectSymbol(AprsSymbolOption option)
    {
        SelectedSymbolOption = option;
    }

    public PortConfig BuildConfig()
    {
        if (!TryCreateSymbol(out _))
        {
            throw new InvalidOperationException("The APRS symbol table and code must each contain one valid character.");
        }

        return new PortConfig
        {
            Type = SelectedPortType,
            Name = Name,
            Server = Server,
            ServerPort = ServerPort,
            Passcode = Passcode,
            Filter = Filter,
            Ssid = Ssid,
            SymbolTableCharacter = UseDefaultSymbol ? null : SymbolTableCharacter,
            SymbolCodeCharacter = UseDefaultSymbol ? null : SymbolCodeCharacter,
            IsRx = IsRx,
            IsTx = IsTx,
            DynamicBeaconMode = UseDefaultBeaconMode ? null : SelectedBeaconMode
        };
    }

    private void UpdateSymbolPreview()
    {
        SymbolPreview?.Dispose();
        SymbolPreview = null;
        IsSymbolValid = false;

        if (!TryCreateSymbol(out var symbol))
        {
            return;
        }

        var symbolBitmap = _symbolBitmapProvider.GetSymbolBitmap(symbol);
        SymbolPreview = _previewFactory(symbolBitmap);
        IsSymbolValid = true;
    }

    private void UpdateSelectedSymbolOption()
    {
        SelectedSymbolOption = SymbolOptions.FirstOrDefault(option =>
            option.TableCharacter == SymbolTableCharacter &&
            option.CodeCharacter == SymbolCodeCharacter);
    }

    private static Bitmap CreatePreviewBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private bool TryCreateSymbol(out Symbol symbol)
    {
        symbol = default;

        if (SymbolTableCharacter.Length != 1 || SymbolCodeCharacter.Length != 1)
        {
            return false;
        }

        try
        {
            symbol = new Symbol(
                SymbolTableCharacter[0].ToSymbolTable(),
                SymbolCodeCharacter[0].ToSymbolCode());
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}

public sealed record AprsSymbolOption(
    string Name,
    string TableCharacter,
    string CodeCharacter);
