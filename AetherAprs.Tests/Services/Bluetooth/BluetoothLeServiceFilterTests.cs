// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Configuration;
using AetherAprs.Services.Bluetooth;
using Xunit;

namespace AetherAprs.Tests.Services.Bluetooth;

public sealed class BluetoothLeServiceFilterTests
{
    [Fact]
    public void Accepts_WhenTheAdvertisementNamesTheNordicUartService_IsTrue() => Assert.True(BluetoothLeServiceFilter.SupportedTransports.Accepts(
            Ad(BluetoothLeKissTransportSettings.DefaultServiceUuid)));

    [Fact]
    public void Accepts_WhenTheAdvertisementNamesTheTrackerService_IsTrue() => Assert.True(BluetoothLeServiceFilter.SupportedTransports.Accepts(
            Ad(BluetoothLeKissTransportSettings.LoRaAprsTrackerServiceUuid)));

    [Fact]
    public void Accepts_WhenOneOfSeveralServicesIsSupported_IsTrue() => Assert.True(BluetoothLeServiceFilter.SupportedTransports.Accepts(
            Ad(ForeignServiceUuid, BluetoothLeKissTransportSettings.DefaultServiceUuid)));

    [Fact]
    public void Accepts_WhenTheAdvertisementNamesNoService_IsTrue() =>
        // Nothing in the advertisement identifies a transport, and the services
        // may only appear after connecting, so the entry has to be kept.
        Assert.True(BluetoothLeServiceFilter.SupportedTransports.Accepts(Ad()));

    [Fact]
    public void Accepts_WhenTheAdvertisementNamesOnlyForeignServices_IsFalse() => Assert.False(BluetoothLeServiceFilter.SupportedTransports.Accepts(Ad(ForeignServiceUuid)));

    [Fact]
    public void Constructor_WithoutServices_RejectsEveryAdvertisedService()
    {
        BluetoothLeServiceFilter filter = new([]);

        Assert.False(filter.Accepts(Ad(BluetoothLeKissTransportSettings.DefaultServiceUuid)));
        Assert.True(filter.Accepts(Ad()));
    }

    [Fact]
    public void Constructor_WithoutACollection_Throws() => Assert.Throws<ArgumentNullException>(() => new BluetoothLeServiceFilter(null!));

    private static readonly Guid ForeignServiceUuid = Guid.Parse("00001812-0000-1000-8000-00805F9B34FB");

    private static BluetoothLeAdvertisement Ad(params Guid[] serviceUuids) =>
        new("00:11:22:33:44:55", "DEVICE", -60, serviceUuids);
}
