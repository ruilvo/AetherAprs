// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;

namespace AetherAprs.Data;

internal static class PortRecordMapper
{
    public static PortRecord ToRecord(PortConfig port)
    {
        var record = new PortRecord
        {
            Id = port.Id,
            Name = port.Name,
            IsEnabled = port.IsEnabled,
            IsRx = port.IsRx,
            IsTx = port.IsTx,
            ShowOnMap = port.ShowOnMap,
            DynamicBeaconMode = port.DynamicBeaconMode
        };
        ApplyTypeSettings(record, port.TypeSettings);
        return record;
    }

    public static PortConfig ToConfig(PortRecord record)
    {
        return new PortConfig
        {
            Id = record.Id,
            Name = record.Name,
            IsEnabled = record.IsEnabled,
            IsRx = record.IsRx,
            IsTx = record.IsTx,
            ShowOnMap = record.ShowOnMap,
            DynamicBeaconMode = record.DynamicBeaconMode,
            TypeSettings = ToTypeSettings(record)
        };
    }

    public static void CopyTo(PortRecord target, PortConfig port)
    {
        target.Name = port.Name;
        target.IsEnabled = port.IsEnabled;
        target.IsRx = port.IsRx;
        target.IsTx = port.IsTx;
        target.ShowOnMap = port.ShowOnMap;
        target.DynamicBeaconMode = port.DynamicBeaconMode;
        ApplyTypeSettings(target, port.TypeSettings);
    }

    private static void ApplyTypeSettings(PortRecord record, IPortTypeSettings? typeSettings)
    {
        switch (typeSettings)
        {
            case AprsIsSettings aprsIs:
                record.Type = "aprs-is";
                record.AprsIs = new AprsIsSettingsRecord
                {
                    Server = aprsIs.Server,
                    ServerPort = aprsIs.ServerPort,
                    Passcode = aprsIs.Passcode,
                    Filter = aprsIs.Filter
                };
                record.Kiss = null;
                break;
            case KissSettings kiss:
                record.Type = "kiss";
                record.AprsIs = null;
                record.Kiss = new KissSettingsRecord
                {
                    Transport = kiss.Transport switch
                    {
                        TcpKissTransportSettings => "tcp",
                        BluetoothClassicKissTransportSettings => "bt-spp",
                        BluetoothLeKissTransportSettings => "ble",
                        _ => string.Empty
                    },
                    Tcp = kiss.Transport as TcpKissTransportSettings is { } tcp
                        ? new TcpKissTransportRecord { Host = tcp.Host, Port = tcp.Port }
                        : null,
                    BluetoothClassic = kiss.Transport as BluetoothClassicKissTransportSettings is { } classic
                        ? new BluetoothClassicKissTransportRecord
                        {
                            DeviceAddress = classic.DeviceAddress,
                            DeviceName = classic.DeviceName
                        }
                        : null,
                    BluetoothLe = kiss.Transport as BluetoothLeKissTransportSettings is { } ble
                        ? new BluetoothLeKissTransportRecord
                        {
                            DeviceAddress = ble.DeviceAddress,
                            DeviceName = ble.DeviceName,
                            ServiceUuid = ble.ServiceUuid,
                            RxCharacteristicUuid = ble.RxCharacteristicUuid,
                            TxCharacteristicUuid = ble.TxCharacteristicUuid
                        }
                        : null
                };
                break;
            default:
                record.Type = null;
                record.AprsIs = null;
                record.Kiss = null;
                break;
        }
    }

    private static IPortTypeSettings? ToTypeSettings(PortRecord record)
    {
        if (record.Type == "aprs-is" && record.AprsIs is { } aprsIs)
        {
            return new AprsIsSettings
            {
                Server = aprsIs.Server,
                ServerPort = aprsIs.ServerPort,
                Passcode = aprsIs.Passcode,
                Filter = aprsIs.Filter
            };
        }

        if (record.Type == "kiss")
        {
            return new KissSettings { Transport = ToKissTransport(record.Kiss) };
        }

        return null;
    }

    private static IKissTransportSettings? ToKissTransport(KissSettingsRecord? kiss)
    {
        if (kiss is null)
        {
            return null;
        }

        if (kiss.Transport == "tcp" && kiss.Tcp is { } tcp)
        {
            return new TcpKissTransportSettings { Host = tcp.Host, Port = tcp.Port };
        }

        if (kiss.Transport == "bt-spp" && kiss.BluetoothClassic is { } classic)
        {
            return new BluetoothClassicKissTransportSettings
            {
                DeviceAddress = classic.DeviceAddress,
                DeviceName = classic.DeviceName
            };
        }

        if (kiss.Transport == "ble" && kiss.BluetoothLe is { } ble)
        {
            return new BluetoothLeKissTransportSettings
            {
                DeviceAddress = ble.DeviceAddress,
                DeviceName = ble.DeviceName,
                ServiceUuid = ble.ServiceUuid,
                RxCharacteristicUuid = ble.RxCharacteristicUuid,
                TxCharacteristicUuid = ble.TxCharacteristicUuid
            };
        }

        return null;
    }
}
