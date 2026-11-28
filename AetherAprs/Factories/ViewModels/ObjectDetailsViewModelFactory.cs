// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory implementation for creating and initializing ObjectDetailsViewModel instances.
/// </summary>
public sealed class ObjectDetailsViewModelFactory(IServiceProvider serviceProvider) : IObjectDetailsViewModelFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public ObjectDetailsViewModel Create(string name)
    {
        ObjectDetailsViewModel viewModel = _serviceProvider.GetRequiredService<ObjectDetailsViewModel>();
        viewModel.Initialize(name);
        return viewModel;
    }
}
