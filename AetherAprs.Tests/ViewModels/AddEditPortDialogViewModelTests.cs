// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Configuration;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public sealed class AddEditPortDialogViewModelTests
{
    [Fact]
    public void DefaultsUseHumanSymbol()
    {
        var viewModel = CreateViewModel();

        Assert.Equal("/", viewModel.SymbolTableCharacter);
        Assert.Equal("[", viewModel.SymbolCodeCharacter);
        Assert.True(viewModel.IsSymbolValid);

        var config = viewModel.BuildConfig();
        Assert.Null(config.SymbolTableCharacter);
        Assert.Null(config.SymbolCodeCharacter);
    }

    [Fact]
    public void ChangingSymbolCharactersUpdatesConfig()
    {
        var viewModel = CreateViewModel();

        viewModel.SymbolTableCharacter = "\\";
        viewModel.SymbolCodeCharacter = ">";
        viewModel.UseDefaultSymbol = false;

        Assert.True(viewModel.IsSymbolValid);
        var config = viewModel.BuildConfig();
        Assert.Equal("\\", config.SymbolTableCharacter);
        Assert.Equal(">", config.SymbolCodeCharacter);
    }

    [Fact]
    public void BuildConfigCanInheritDefaultSymbolAndBeaconMode()
    {
        var viewModel = CreateViewModel();

        var config = viewModel.BuildConfig();

        Assert.Null(config.SymbolTableCharacter);
        Assert.Null(config.SymbolCodeCharacter);
        Assert.Null(config.DynamicBeaconMode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("x")]
    public void InvalidTableCharacterDisablesSymbolAndRejectsConfig(string tableCharacter)
    {
        var viewModel = CreateViewModel();
        viewModel.SymbolTableCharacter = tableCharacter;

        if (tableCharacter == "/")
        {
            Assert.True(viewModel.IsSymbolValid);
            return;
        }

        Assert.False(viewModel.IsSymbolValid);
        Assert.Throws<InvalidOperationException>(() => viewModel.BuildConfig());
    }

    [Fact]
    public void InvalidCodeCharacterDisablesSymbolAndRejectsConfig()
    {
        var viewModel = CreateViewModel();
        viewModel.SymbolCodeCharacter = "\x1F";

        Assert.False(viewModel.IsSymbolValid);
        Assert.Throws<InvalidOperationException>(() => viewModel.BuildConfig());
    }

    [Fact]
    public void BuildConfigForKissTcpProducesKissSettings()
    {
        var viewModel = CreateViewModel();

        viewModel.SelectedPortSettingsType = typeof(KissSettings);
        viewModel.SelectedKissTransportType = typeof(TcpKissTransportSettings);
        viewModel.TcpHost = "10.0.0.5";
        viewModel.TcpPort = 8001;
        viewModel.Name = "KISS TCP";

        var config = viewModel.BuildConfig();

        var kiss = Assert.IsType<KissSettings>(config.TypeSettings);
        var tcp = Assert.IsType<TcpKissTransportSettings>(kiss.Transport);
        Assert.Equal("10.0.0.5", tcp.Host);
        Assert.Equal(8001, tcp.Port);
    }

    private sealed class FakeKissStreamFactory : IKissStreamFactory
    {
        public IReadOnlyCollection<Type> SupportedTransports { get; } = [typeof(TcpKissTransportSettings)];

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

    private static AddEditPortDialogViewModel CreateViewModel() =>
        new(
            "CT7ALW",
            1,
            new FakeKissStreamFactory(),
            new FakeBluetoothLeScanner(),
            new FakeBluetoothClassicDeviceProvider());
}