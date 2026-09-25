// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Services;
using AetherAprs.Services.Bluetooth;
using AetherAprs.Transports.Kiss;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AetherAprs.Factories;

/// <summary>
/// Factory implementation that creates AddEditPortViewModel instances from the DI container
/// and initializes them with context-specific data.
/// </summary>
public class AddEditPortViewModelFactory : IAddEditPortViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AddEditPortViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public AddEditPortViewModel CreateForAdd(string callsign, int portNumber)
    {
        var vm = _serviceProvider.GetRequiredService<AddEditPortViewModel>();
        vm.Initialize(callsign, portNumber);
        return vm;
    }

    public AddEditPortViewModel CreateForEdit(string callsign, int portNumber, PortConfig existingConfig)
    {
        var vm = _serviceProvider.GetRequiredService<AddEditPortViewModel>();
        vm.Initialize(callsign, portNumber, existingConfig);
        return vm;
    }
}
