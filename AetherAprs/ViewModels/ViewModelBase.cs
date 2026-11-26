// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace AetherAprs.ViewModels;

/// <summary>
/// Base type for view models. Validation attributes on view model properties are
/// surfaced through <see cref="ObservableValidator"/> and the toolkit's
/// <c>NotifyDataErrorInfo</c> support.
/// </summary>
/// <remarks>
/// <see cref="ObservableValidator"/> reflects over the attributes declared on a
/// property the first time it validates one, which the trimmer cannot prove from
/// here. Every view model is a public type that the container and the views
/// reference statically, so its properties and their attributes are preserved;
/// the suppression records that reasoning instead of dropping validation.
/// </remarks>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026",
    Justification = "View models are statically referenced types; their validated properties and attributes are preserved.")]
public abstract class ViewModelBase : ObservableValidator
{
}
