// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels;

public partial class HomeViewModel : ViewModelBase
{

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartAprsIsConnectionCommand))]
    public partial string Callsign { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartAprsIsConnectionCommand))]
    public partial string AprsIsPasscode { get; set; } = string.Empty;

    public IRelayCommand StartAprsIsConnectionCommand { get; }

    public HomeViewModel()
    {
        StartAprsIsConnectionCommand = new RelayCommand(StartAprsIsConnection, CanStartAprsIsConnection);
    }

    private void StartAprsIsConnection()
    {
    }

    private bool CanStartAprsIsConnection()
        => !string.IsNullOrWhiteSpace(Callsign) && !string.IsNullOrWhiteSpace(AprsIsPasscode);
}
