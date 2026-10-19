// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory implementation for creating and initializing MessageTemplateEditViewModel instances.
/// </summary>
public class MessageTemplateEditViewModelFactory : IMessageTemplateEditViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public MessageTemplateEditViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public MessageTemplateEditViewModel Create(MessageTemplate? original, IReadOnlyList<string> existingNames)
    {
        var viewModel = _serviceProvider.GetRequiredService<MessageTemplateEditViewModel>();
        viewModel.Initialize(original, existingNames);
        return viewModel;
    }
}
