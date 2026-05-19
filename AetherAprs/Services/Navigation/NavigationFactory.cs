// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Services.Navigation;

public sealed class NavigationFactory(IServiceProvider services) : INavigationFactory
{
    public T Create<T>() where T : ObservableObject => services.GetRequiredService<T>();
}
