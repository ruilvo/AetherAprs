// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory implementation for creating and initializing MessageTemplateDialogViewModel instances.
/// </summary>
public class MessageTemplateDialogViewModelFactory : IMessageTemplateDialogViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public MessageTemplateDialogViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public MessageTemplateDialogViewModel Create(IReadOnlyList<MessageTemplate> templates, string? destination)
    {
        var viewModel = _serviceProvider.GetRequiredService<MessageTemplateDialogViewModel>();
        viewModel.Initialize(templates, destination);
        return viewModel;
    }
}
