// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories;

/// <summary>
/// Factory implementation for creating and initializing PacketDetailsViewModel instances.
/// </summary>
public class PacketDetailsViewModelFactory : IPacketDetailsViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public PacketDetailsViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ViewModels.Pages.PacketDetailsViewModel Create(string source)
    {
        var viewModel = _serviceProvider.GetRequiredService<ViewModels.Pages.PacketDetailsViewModel>();
        viewModel.Initialize(source);
        return viewModel;
    }
}
