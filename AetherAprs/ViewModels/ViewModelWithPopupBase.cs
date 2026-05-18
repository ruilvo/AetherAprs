// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

public abstract partial class ViewModelWithPopupBase : ViewModelBase
{
    [ObservableProperty]
    public partial ObservableObject? ActivePopup { get; set; }

    public void ClosePopup() => ActivePopup = null;
}