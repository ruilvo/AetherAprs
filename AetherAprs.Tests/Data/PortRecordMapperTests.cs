// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Linq;
using AetherAprs.Configuration;
using AetherAprs.Data;
using AetherAprs.Data.Mappers;
using AetherAprs.Models;
using AetherAprs.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AetherAprs.Tests.Data;

public sealed class PortRecordMapperTests
{
    [Fact]
    public void RoundTripPersistsAprsIsSettings()
    {
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "APRS-IS",
            IsEnabled = true,
            IsRx = true,
            IsTx = false,
            ShowOnMap = false,
            TypeSettings = new AprsIsSettings
            {
                Server = "rotate.aprs2.net",
                ServerPort = 14580,
                Passcode = "12345",
                Filter = "m/50"
            }
        };

        using TempAppDatabase db = TempAppDatabase.Create(port);
        using AppDbContext context = db.CreateContext();
        PortConfig loaded = PortRecordMapper.ToConfig(Assert.Single(context.Ports.AsNoTracking()));

        Assert.Equal(port.Id, loaded.Id);
        Assert.Equal("APRS-IS", loaded.Name);
        Assert.True(loaded.IsEnabled);
        Assert.True(loaded.IsRx);
        Assert.False(loaded.IsTx);
        Assert.False(loaded.ShowOnMap);
        AprsIsSettings aprsIs = Assert.IsType<AprsIsSettings>(loaded.TypeSettings);
        Assert.Equal("rotate.aprs2.net", aprsIs.Server);
        Assert.Equal(14580, aprsIs.ServerPort);
        Assert.Equal("12345", aprsIs.Passcode);
        Assert.Equal("m/50", aprsIs.Filter);
        Assert.Equal(AprsIsSecurityMode.Plaintext, aprsIs.Security);
    }

    [Fact]
    public void RoundTripPersistsAprsIsTlsSecurity()
    {
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "APRS-IS TLS",
            IsEnabled = true,
            IsRx = true,
            IsTx = true,
            ShowOnMap = false,
            TypeSettings = new AprsIsSettings
            {
                Server = "rotate.aprs2.net",
                ServerPort = 14580,
                Passcode = "12345",
                Filter = "m/50",
                Security = AprsIsSecurityMode.Tls
            }
        };

        using TempAppDatabase db = TempAppDatabase.Create(port);
        using AppDbContext context = db.CreateContext();
        PortConfig loaded = PortRecordMapper.ToConfig(Assert.Single(context.Ports.AsNoTracking()));

        AprsIsSettings aprsIs = Assert.IsType<AprsIsSettings>(loaded.TypeSettings);
        Assert.Equal(AprsIsSecurityMode.Tls, aprsIs.Security);
    }

    [Fact]
    public void RoundTripPersistsKissTcpTransport()
    {
        PortConfig port = new()
        {
            Id = Guid.NewGuid(),
            Name = "KISS TCP",
            TypeSettings = new KissSettings
            {
                Transport = new TcpKissTransportSettings { Host = "10.0.0.5", Port = 8001 }
            }
        };

        using TempAppDatabase db = TempAppDatabase.Create(port);
        using AppDbContext context = db.CreateContext();
        PortConfig loaded = PortRecordMapper.ToConfig(Assert.Single(context.Ports.AsNoTracking()));
        KissSettings kiss = Assert.IsType<KissSettings>(loaded.TypeSettings);
        TcpKissTransportSettings tcp = Assert.IsType<TcpKissTransportSettings>(kiss.Transport);
        Assert.Equal("10.0.0.5", tcp.Host);
        Assert.Equal(8001, tcp.Port);
    }

    [Fact]
    public void RoundTripPersistsBluetoothClassicAndLeTransports()
    {
        PortConfig classicPort = new()
        {
            Id = Guid.NewGuid(),
            Name = "KISS SPP",
            TypeSettings = new KissSettings
            {
                Transport = new BluetoothClassicKissTransportSettings
                {
                    DeviceAddress = "AA:BB:CC:DD:EE:FF",
                    DeviceName = "TNC"
                }
            }
        };
        Guid serviceUuid = Guid.NewGuid();
        Guid rxUuid = Guid.NewGuid();
        Guid txUuid = Guid.NewGuid();
        PortConfig blePort = new()
        {
            Id = Guid.NewGuid(),
            Name = "KISS BLE",
            TypeSettings = new KissSettings
            {
                Transport = new BluetoothLeKissTransportSettings
                {
                    DeviceAddress = "11:22:33:44:55:66",
                    DeviceName = "BLE TNC",
                    ServiceUuid = serviceUuid,
                    RxCharacteristicUuid = rxUuid,
                    TxCharacteristicUuid = txUuid
                }
            }
        };

        using TempAppDatabase db = TempAppDatabase.Create(classicPort, blePort);
        using AppDbContext context = db.CreateContext();
        PortConfig loadedClassic = PortRecordMapper.ToConfig(context.Ports.AsNoTracking().Single(p => p.Id == classicPort.Id));
        PortConfig loadedBle = PortRecordMapper.ToConfig(context.Ports.AsNoTracking().Single(p => p.Id == blePort.Id));

        BluetoothClassicKissTransportSettings classic = Assert.IsType<BluetoothClassicKissTransportSettings>(
            Assert.IsType<KissSettings>(loadedClassic.TypeSettings).Transport);
        Assert.Equal("AA:BB:CC:DD:EE:FF", classic.DeviceAddress);
        Assert.Equal("TNC", classic.DeviceName);

        BluetoothLeKissTransportSettings ble = Assert.IsType<BluetoothLeKissTransportSettings>(
            Assert.IsType<KissSettings>(loadedBle.TypeSettings).Transport);
        Assert.Equal("11:22:33:44:55:66", ble.DeviceAddress);
        Assert.Equal("BLE TNC", ble.DeviceName);
        Assert.Equal(serviceUuid, ble.ServiceUuid);
        Assert.Equal(rxUuid, ble.RxCharacteristicUuid);
        Assert.Equal(txUuid, ble.TxCharacteristicUuid);
    }
}
