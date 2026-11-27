// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherAprs.ViewModels;

/// <summary>
/// Base type for view models. Exposes validation errors through
/// <see cref="INotifyDataErrorInfo"/> so Avalonia's data validation surfaces them.
/// </summary>
/// <remarks>
/// The errors are stored and published by hand instead of going through
/// <c>ObservableValidator</c>. That type reflects over the validation attributes
/// declared on a property, which the IL linker cannot prove from a view model and
/// which produced <c>IL2026</c> once full trimming was enabled. A view model here
/// only needs to publish the messages a validator already computed, so the small
/// amount of state below removes the reflection completely.
/// </remarks>
public abstract class ViewModelBase : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, IReadOnlyList<string>> _errors =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    /// <inheritdoc />
    public bool HasErrors => _errors.Count > 0;

    /// <inheritdoc />
    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return Array.Empty<string>();
        }

        return _errors.TryGetValue(propertyName, out IReadOnlyList<string>? errors)
            ? errors
            : Array.Empty<string>();
    }

    /// <summary>
    /// Replaces the validation messages for one property and raises
    /// <see cref="ErrorsChanged"/> when they actually changed.
    /// </summary>
    /// <param name="propertyName">Name of the validated property.</param>
    /// <param name="errors">Messages for that property; empty when it is valid.</param>
    protected void SetErrors(string propertyName, IReadOnlyList<string> errors)
    {
        bool known = _errors.TryGetValue(propertyName, out IReadOnlyList<string>? previous);

        if (errors.Count == 0)
        {
            if (!known)
            {
                return;
            }

            _errors.Remove(propertyName);
        }
        else
        {
            if (known && previous!.SequenceEqual(errors))
            {
                return;
            }

            _errors[propertyName] = errors;
        }

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }
}
