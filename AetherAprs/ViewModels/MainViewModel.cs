// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace AetherAprs.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>
    ///  Used for design-time data binding in Avalonia XAML.
    /// This constructor is not intended for use in production code.
    /// </summary>
    public MainViewModel() : this(null!) { }

    public MainViewModel(ILogger<MainViewModel> logger)
    {
        _logger = logger;
    }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
