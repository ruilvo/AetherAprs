// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Models.Aprs;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories.ViewModels;

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

    public ConversationViewModel Create(Callsign callsign)
    {
        var viewModel = _serviceProvider.GetRequiredService<ConversationViewModel>();
        viewModel.Initialize(callsign);
        return viewModel;
    }

    public ConversationViewModel CreateNew()
    {
        var viewModel = _serviceProvider.GetRequiredService<ConversationViewModel>();
        viewModel.InitializeNew();
        return viewModel;
    }
}
