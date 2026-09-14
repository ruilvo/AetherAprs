// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.Services.Bluetooth;
using Xunit;

namespace AetherAprs.Tests.Services.Bluetooth;

public sealed class UnsupportedBluetoothStubTests
{
    [Fact]
    public void ClassicDeviceProvider_IsUnsupported()
    {
        var provider = new UnsupportedBluetoothClassicDeviceProvider();
        Assert.False(provider.IsSupported);
    }

    [Fact]
    public async Task ClassicDeviceProvider_EnsurePermissionAsync_Throws()
    {
        var provider = new UnsupportedBluetoothClassicDeviceProvider();

        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            provider.EnsurePermissionAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Bluetooth Classic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClassicDeviceProvider_GetBondedDevicesAsync_Throws()
    {
        var provider = new UnsupportedBluetoothClassicDeviceProvider();

        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            provider.GetBondedDevicesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Bluetooth Classic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeScanner_IsUnsupported()
    {
        var scanner = new UnsupportedBluetoothLeScanner();
        Assert.False(scanner.IsSupported);
    }

    [Fact]
    public async Task LeScanner_EnsurePermissionAsync_Throws()
    {
        var scanner = new UnsupportedBluetoothLeScanner();

        var ex = await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
            scanner.EnsurePermissionAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Bluetooth LE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeScanner_ScanAsync_Throws()
    {
        var scanner = new UnsupportedBluetoothLeScanner();

        var ex = Assert.Throws<PlatformNotSupportedException>(() =>
            scanner.ScanAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Bluetooth LE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
