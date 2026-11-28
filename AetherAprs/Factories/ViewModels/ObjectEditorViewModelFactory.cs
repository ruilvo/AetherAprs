// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;

using AetherAprs.Models.Aprs.Packets;
using AetherAprs.ViewModels.Pages;

using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories.ViewModels;

/// <summary>
/// Factory implementation for creating and initializing ObjectEditorViewModel instances.
/// </summary>
public sealed class ObjectEditorViewModelFactory(IServiceProvider serviceProvider) : IObjectEditorViewModelFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public ObjectEditorViewModel Create() => Create(null);

    public ObjectEditorViewModel Create(ObjectPacket? existing)
    {
        ObjectEditorViewModel viewModel = _serviceProvider.GetRequiredService<ObjectEditorViewModel>();
        viewModel.Initialize(existing);
        return viewModel;
    }
}
