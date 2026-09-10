// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using AetherAprs.Configuration;
using AetherAprs.Helpers;

namespace AetherAprs.ViewModels;

public partial class AddEditPortDialogViewModel(string globalCallsign, int nextPortNumber) : ViewModelBase
{

    [ObservableProperty]
    public partial string Name { get; set; } = $"APRS-IS Port {nextPortNumber}";

    [ObservableProperty]
    public partial PortType SelectedPortType { get; set; } = PortType.AprsIs;

    [ObservableProperty]
    public partial string Server { get; set; } = "euro.aprs2.net";

    [ObservableProperty]
    public partial int ServerPort { get; set; } = 14580;

    [ObservableProperty]
    public partial string Passcode { get; set; } = AprsPasscode.Compute(globalCallsign);

    [ObservableProperty]
    public partial string Filter { get; set; } = "m/50";

    [ObservableProperty]
    public partial int? Ssid { get; set; }

    [ObservableProperty]
    public partial bool IsRx { get; set; } = true;

    [ObservableProperty]
    public partial bool IsTx { get; set; } = false;

    public PortType[] PortTypes { get; } = [PortType.AprsIs];

    public PortConfig BuildConfig()
    {
        return new PortConfig
        {
            Type = SelectedPortType,
            Name = Name,
            Server = Server,
            ServerPort = ServerPort,
            Passcode = Passcode,
            Filter = Filter,
            Ssid = Ssid,
            IsRx = IsRx,
            IsTx = IsTx
        };
    }
}
