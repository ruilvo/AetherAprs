// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using AetherAprs.ViewModels;
using AetherAprs.Views;
using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var app = Application.Current as App;
        if (app?.Services is null)
        {
            return new TextBlock { Text = "Services not initialized" };
        }

        return param switch
        {
            MainViewModel => app.Services.GetRequiredService<MainView>(),
            _ => new TextBlock { Text = "Not Found: " + param.GetType().Name }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
