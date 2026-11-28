// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Configuration;
using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory implementation that creates AddEditPortViewModel instances from the DI container
/// and initializes them with context-specific data.
/// </summary>
public class AddEditPortViewModelFactory(IServiceProvider serviceProvider) : IAddEditPortViewModelFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public AddEditPortViewModel CreateForAdd(string callsign, int portNumber)
    {
        AddEditPortViewModel vm = _serviceProvider.GetRequiredService<AddEditPortViewModel>();
        vm.Initialize(callsign, portNumber);
        return vm;
    }

    public AddEditPortViewModel CreateForEdit(string callsign, int portNumber, PortConfig existingConfig)
    {
        AddEditPortViewModel vm = _serviceProvider.GetRequiredService<AddEditPortViewModel>();
        vm.Initialize(callsign, portNumber, existingConfig);
        return vm;
    }
}
