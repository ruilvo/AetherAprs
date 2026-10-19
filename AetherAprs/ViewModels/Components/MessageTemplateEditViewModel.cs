// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Services.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;

namespace AetherAprs.ViewModels.Components;

/// <summary>
/// View model for the add/edit template dialog. Initialized via
/// <see cref="Initialize"/> with the original template (null for add mode)
/// and the existing template names used for duplicate detection; validates
/// the fields and exposes a confirm command that produces the resulting
/// <see cref="MessageTemplate"/>.
/// </summary>
public sealed partial class MessageTemplateEditViewModel : ViewModelBase
{
    private IReadOnlyList<string> _existingNames = [];
    private string? _originalName;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Destination { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    /// <summary>
    /// Gets the validation error for the name field, or null when valid.
    /// </summary>
    public string? NameError { get; private set; }

    /// <summary>
    /// Gets the validation error for the destination field, or null when valid.
    /// </summary>
    public string? DestinationError { get; private set; }

    /// <summary>
    /// Gets the validation error for the body field, or null when valid.
    /// </summary>
    public string? BodyError { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the dialog was opened in add mode
    /// (no original template).
    /// </summary>
    public bool IsAddMode { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the current input is valid and can be saved.
    /// </summary>
    public bool CanSave => NameError == null && DestinationError == null && BodyError == null;

    /// <summary>
    /// Gets the result of the dialog, or null until saved.
    /// </summary>
    public MessageTemplate? Result { get; private set; }

    /// <summary>
    /// Raised when the user saves, so the view can close the dialog.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Initializes the dialog with the original template to edit (null for
    /// add mode) and the names of all existing templates for duplicate
    /// detection (the original's own name is excluded).
    /// </summary>
    public void Initialize(MessageTemplate? original, IReadOnlyList<string> existingNames)
    {
        _originalName = original?.Name;
        _existingNames = existingNames ?? [];
        IsAddMode = original == null;
        Name = original?.Name ?? string.Empty;
        Destination = original?.Destination ?? string.Empty;
        Body = original?.Body ?? string.Empty;

        OnPropertyChanged(nameof(IsAddMode));
        ValidateAll();
    }

    partial void OnNameChanged(string value) => ValidateName();

    partial void OnDestinationChanged(string value) => ValidateDestination();

    partial void OnBodyChanged(string value) => ValidateBody();

    /// <summary>
    /// Builds a template from the current input with trimmed values.
    /// </summary>
    public MessageTemplate BuildTemplate() => new()
    {
        Name = Name.Trim(),
        Destination = Destination?.Trim() ?? string.Empty,
        Body = Body?.Trim() ?? string.Empty
    };

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            return;
        }

        Result = BuildTemplate();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ValidateAll()
    {
        ValidateName();
        ValidateDestination();
        ValidateBody();
    }

    private void ValidateName()
    {
        var trimmed = Name?.Trim();
        string? error;
        if (string.IsNullOrEmpty(trimmed))
        {
            error = Strings.Get("ValidationTemplateNameRequired");
        }
        else if (IsDuplicateName(trimmed))
        {
            error = Strings.Get("ValidationTemplateNameDuplicate");
        }
        else
        {
            error = null;
        }

        SetError(error, nameof(NameError), () => NameError, value => NameError = value);
    }

    private bool IsDuplicateName(string trimmedName)
    {
        var original = _originalName?.Trim();
        foreach (var existing in _existingNames)
        {
            var trimmedExisting = existing?.Trim();
            if (string.Equals(trimmedExisting, trimmedName, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(trimmedExisting, original, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ValidateDestination()
    {
        // The destination is optional; when present it must be a valid callsign.
        var trimmed = Destination?.Trim();
        string? error = null;
        if (!string.IsNullOrEmpty(trimmed) && !Callsign.TryParse(trimmed, out _))
        {
            error = Strings.Get("InvalidDestinationCallsign");
        }

        SetError(error, nameof(DestinationError), () => DestinationError, value => DestinationError = value);
    }

    private void ValidateBody()
    {
        string? error = null;
        if ((Body?.Length ?? 0) > MessageTemplateService.MaxTemplateMessageLength)
        {
            error = Strings.Format("ValidationTemplateBodyMaxLength", MessageTemplateService.MaxTemplateMessageLength);
        }

        SetError(error, nameof(BodyError), () => BodyError, value => BodyError = value);
    }

    private void SetError(string? error, string errorPropertyName, Func<string?> getError, Action<string?> setError)
    {
        if (string.Equals(getError(), error, StringComparison.Ordinal))
        {
            return;
        }

        setError(error);
        OnPropertyChanged(errorPropertyName);
        OnPropertyChanged(nameof(CanSave));
    }
}
