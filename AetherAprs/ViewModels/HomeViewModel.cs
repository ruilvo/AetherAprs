// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    private readonly RelayCommand _startAprsIsConnectionCommand;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartAprsIsConnectionCommand))]
    public partial string Callsign { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartAprsIsConnectionCommand))]
    public partial string AprsIsPasscode { get; set; } = string.Empty;

    public HomeViewModel()
    {
        Title = "Aether APRS";
        _startAprsIsConnectionCommand = new RelayCommand(StartAprsIsConnection, CanStartAprsIsConnection);
    }

    public IRelayCommand StartAprsIsConnectionCommand => _startAprsIsConnectionCommand;

    private void StartAprsIsConnection()
    {
        Title = $"Connecting APRS-IS as {Callsign}";
    }

    private bool CanStartAprsIsConnection()
        => !string.IsNullOrWhiteSpace(Callsign) && !string.IsNullOrWhiteSpace(AprsIsPasscode);
}
