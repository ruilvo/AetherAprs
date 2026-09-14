// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Imaging;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using SkiaSharp;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class AddEditPortDialogViewModelTests
{
    [Fact]
    public void DefaultsUseHumanSymbolAndCreatePreview()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        Assert.Equal("/", viewModel.SymbolTableCharacter);
        Assert.Equal("[", viewModel.SymbolCodeCharacter);
        Assert.True(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Equal(1, provider.RequestCount);

        var config = viewModel.BuildConfig();
        Assert.Null(config.SymbolTableCharacter);
        Assert.Null(config.SymbolCodeCharacter);
    }

    [Fact]
    public void ChangingSymbolCharactersUpdatesPreviewAndConfig()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        viewModel.SymbolTableCharacter = "\\";
        viewModel.SymbolCodeCharacter = ">";
        viewModel.UseDefaultSymbol = false;

        Assert.True(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Equal(3, provider.RequestCount);
        var config = viewModel.BuildConfig();
        Assert.Equal("\\", config.SymbolTableCharacter);
        Assert.Equal(">", config.SymbolCodeCharacter);
    }

    [Fact]
    public void SelectingSymbolOptionUpdatesBothCharacters()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);
        var ambulance = Assert.Single(viewModel.SymbolOptions, option => option.Name == "Ambulance");

        viewModel.SelectedSymbolOption = ambulance;

        Assert.Equal("/", viewModel.SymbolTableCharacter);
        Assert.Equal("a", viewModel.SymbolCodeCharacter);
        Assert.Same(ambulance, viewModel.SelectedSymbolOption);
    }

    [Fact]
    public void BuildConfigCanInheritDefaultSymbolAndBeaconMode()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        var config = viewModel.BuildConfig();

        Assert.Null(config.SymbolTableCharacter);
        Assert.Null(config.SymbolCodeCharacter);
        Assert.Null(config.DynamicBeaconMode);
    }

    [Fact]
    public void ManuallyEditingSymbolClearsPresetSelection()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        viewModel.SymbolCodeCharacter = "#";

        Assert.Null(viewModel.SelectedSymbolOption);
        Assert.True(viewModel.IsSymbolValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("x")]
    public void InvalidTableCharacterDisablesSymbolAndRejectsConfig(string tableCharacter)
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);
        viewModel.SymbolTableCharacter = tableCharacter;

        if (tableCharacter == "/")
        {
            Assert.True(viewModel.IsSymbolValid);
            Assert.Equal(1, provider.RequestCount);
            return;
        }

        Assert.False(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Throws<InvalidOperationException>(() => viewModel.BuildConfig());
    }

    [Fact]
    public void InvalidCodeCharacterDisablesSymbolAndRejectsConfig()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);
        viewModel.SymbolCodeCharacter = "\x1F";

        Assert.False(viewModel.IsSymbolValid);
        Assert.Null(viewModel.SymbolPreview);
        Assert.Throws<InvalidOperationException>(() => viewModel.BuildConfig());
    }

    [Fact]
    public void BuildConfigForKissTcpProducesKissSettings()
    {
        using var provider = new TestSymbolBitmapProvider();
        var viewModel = CreateViewModel(provider);

        viewModel.SelectedPortType = PortType.Kiss;
        viewModel.SelectedKissTransportKind = KissTransportKind.Tcp;
        viewModel.TcpHost = "10.0.0.5";
        viewModel.TcpPort = 8001;
        viewModel.Name = "KISS TCP";

        var config = viewModel.BuildConfig();

        Assert.Equal(PortType.Kiss, config.Type);
        var kiss = Assert.IsType<KissSettings>(config.TypeSettings);
        Assert.Equal(KissTransportKind.Tcp, kiss.TransportKind);
        var tcp = Assert.IsType<TcpKissTransportSettings>(kiss.Transport);
        Assert.Equal("10.0.0.5", tcp.Host);
        Assert.Equal(8001, tcp.Port);
    }

    private sealed class TestSymbolBitmapProvider : IAprsSymbolBitmapProvider, IDisposable
    {
        public int RequestCount { get; private set; }

        public SKBitmap GetSymbolBitmap(Symbol symbol)
        {
            RequestCount++;
            return new SKBitmap(8, 8);
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeKissStreamFactory : IKissStreamFactory
    {
        public IReadOnlyCollection<KissTransportKind> SupportedTransports { get; } = [KissTransportKind.Tcp];

        public Task<System.IO.Stream> OpenAsync(KissSettings settings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBluetoothLeScanner : IBluetoothLeScanner
    {
        public bool IsSupported => false;

        public Task EnsurePermissionAsync(CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException();

        public IAsyncEnumerable<BluetoothLeAdvertisement> ScanAsync(CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException();
    }

    private sealed class FakeBluetoothClassicDeviceProvider : IBluetoothClassicDeviceProvider
    {
        public bool IsSupported => false;

        public Task EnsurePermissionAsync(CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException();

        public Task<IReadOnlyList<BluetoothClassicDevice>> GetBondedDevicesAsync(CancellationToken cancellationToken = default) =>
            throw new PlatformNotSupportedException();
    }

    private static AddEditPortDialogViewModel CreateViewModel(TestSymbolBitmapProvider provider) =>
        new(
            "CT7ALW",
            1,
            provider,
            new FakeKissStreamFactory(),
            new FakeBluetoothLeScanner(),
            new FakeBluetoothClassicDeviceProvider(),
            _ => null);
}