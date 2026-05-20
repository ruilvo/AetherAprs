// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

/// <summary>
/// Base class for all view models in the application.
/// Provides property change notification via <see cref="ObservableObject"/>.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
}

