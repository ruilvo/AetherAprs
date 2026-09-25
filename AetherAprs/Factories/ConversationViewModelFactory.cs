// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories;

/// <summary>
/// Factory implementation for creating and initializing ConversationViewModel instances.
/// </summary>
public class ConversationViewModelFactory : IConversationViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ConversationViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ViewModels.Pages.ConversationViewModel Create(Callsign callsign)
    {
        var viewModel = _serviceProvider.GetRequiredService<ViewModels.Pages.ConversationViewModel>();
        viewModel.Initialize(callsign);
        return viewModel;
    }

    public ViewModels.Pages.ConversationViewModel CreateNew()
    {
        var viewModel = _serviceProvider.GetRequiredService<ViewModels.Pages.ConversationViewModel>();
        viewModel.InitializeNew();
        return viewModel;
    }
}
