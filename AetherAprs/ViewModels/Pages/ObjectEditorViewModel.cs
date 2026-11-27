// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Helpers;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Objects;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Geo;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the object create/edit page. Initialized via
/// <see cref="Initialize"/> with an existing object (edit/kill mode) or null
/// (add mode). The kill switch routes to <see cref="IObjectService.KillAsync"/>
/// because <see cref="IObjectService.CreateOrUpdateAsync"/> always transmits a
/// live object.
/// </summary>
public partial class ObjectEditorViewModel(
    IObjectService objectService,
    INavigationService navigationService,
    AprsSymbolPickerViewModel symbolPicker,
    ILogger<ObjectEditorViewModel>? logger = null) : ViewModelBase
{
    private readonly IObjectService _objectService = objectService ?? throw new ArgumentNullException(nameof(objectService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly ILogger<ObjectEditorViewModel>? _logger = logger;
    private ObjectPacket? _existing;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LatitudeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LongitudeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Comment { get; set; }

    [ObservableProperty]
    public partial AprsSymbolPickerViewModel SymbolPicker { get; set; } = symbolPicker ?? throw new ArgumentNullException(nameof(symbolPicker));

    [ObservableProperty]
    public partial bool KillObject { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets whether the editor was opened for an existing object.
    /// </summary>
    public bool IsEditing => _existing is not null;

    public string Title => IsEditing ? Strings.Get("EditObject") : Strings.Get("AddObject");

    /// <summary>
    /// Initializes the editor with the object to edit (or null to add a new one).
    /// </summary>
    public void Initialize(ObjectPacket? existing)
    {
        _existing = existing;

        SymbolPicker.BeginSuppressNotifications();
        if (existing is null)
        {
            Name = string.Empty;
            LatitudeText = string.Empty;
            LongitudeText = string.Empty;
            Comment = null;
            SymbolPicker.TableCharacter = "/";
            SymbolPicker.CodeCharacter = "[";
        }
        else
        {
            Name = existing.Name;
            LatitudeText = existing.Location.Latitude.ToString("F6", CultureInfo.InvariantCulture);
            LongitudeText = existing.Location.Longitude.ToString("F6", CultureInfo.InvariantCulture);
            Comment = existing.Comment;
            SymbolPicker.TableCharacter = existing.Symbol.Table.ToChar().ToString();
            SymbolPicker.CodeCharacter = existing.Symbol.Code.ToChar().ToString();
        }

        SymbolPicker.EndSuppressNotifications();
        KillObject = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private void Cancel() => _navigationService.GoBack();

    private bool CanSave() => !IsSaving;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (IsSaving)
        {
            return;
        }

        ErrorMessage = null;
        string? name = Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorMessage = Strings.Get("ValidationObjectNameRequired");
            return;
        }

        if (name.Length > 9)
        {
            ErrorMessage = Strings.Get("ValidationObjectNameMaxLength");
            return;
        }

        IsSaving = true;
        try
        {
            if (KillObject)
            {
                await _objectService.KillAsync(name);
            }
            else
            {
                if (!TryParseCoordinate(LatitudeText, out double latitude))
                {
                    ErrorMessage = Strings.Get("ValidationObjectLatitudeInvalid");
                    return;
                }

                if (!TryParseCoordinate(LongitudeText, out double longitude))
                {
                    ErrorMessage = Strings.Get("ValidationObjectLongitudeInvalid");
                    return;
                }

                await _objectService.CreateOrUpdateAsync(
                    name,
                    new Coordinate(latitude, longitude),
                    SymbolPicker.TableCharacter[0].ToSymbolTable(),
                    SymbolPicker.CodeCharacter[0].ToSymbolCode(),
                    Comment);
            }

            _navigationService.GoBack();
        }
        catch (Exception ex)
        {
            // Surface the failure instead of silently swallowing it; the user
            // stays on the form and the button remains usable for a retry.
            ErrorMessage = UserFacingFailure.Describe(ex);
            _logger?.LogError(ex, "Failed to save or kill object {ObjectName}", name);
        }
        finally
        {
            IsSaving = false;
            SaveCommand.NotifyCanExecuteChanged();
        }
    }

    private static bool TryParseCoordinate(string? text, out double value) => double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
}
