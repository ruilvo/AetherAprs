// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using AetherAprs.Factories.ViewModels;
using AetherAprs.Helpers;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Objects;
using AetherAprs.Services.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the APRS objects management page. Lists known objects (live
/// and killed), opens the object editor for add/edit, and kills objects.
/// </summary>
public sealed partial class ObjectsViewModel(
    IObjectService objectService,
    INavigationService navigationService,
    IObjectEditorViewModelFactory editorFactory,
    IConfirmationDialogService confirmationDialogService,
    ILogger<ObjectsViewModel>? logger = null) : ViewModelBase
{
    private readonly IObjectService _objectService = objectService ?? throw new ArgumentNullException(nameof(objectService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly IObjectEditorViewModelFactory _editorFactory = editorFactory ?? throw new ArgumentNullException(nameof(editorFactory));
    private readonly IConfirmationDialogService _confirmationDialogService = confirmationDialogService ?? throw new ArgumentNullException(nameof(confirmationDialogService));
    private readonly ILogger<ObjectsViewModel>? _logger = logger;

    [ObservableProperty]
    public partial ObservableCollection<ObjectPacket> Objects { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Objects"/> has any items.
    /// Updated whenever the collection is mutated so the page bindings do not
    /// go stale on in-place changes.
    /// </summary>
    [ObservableProperty]
    public partial bool HasObjects { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Objects"/> is empty.
    /// </summary>
    [ObservableProperty]
    public partial bool IsObjectsEmpty { get; set; } = true;

    /// <summary>
    /// Gets the reason the last kill request failed, or an empty string when the
    /// last action succeeded. Killing an object is the only action on this page that
    /// can fail after confirmation, so the text belongs next to the list.
    /// </summary>
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Reloads the object list from the object service. Called by the page
    /// code-behind when the page is shown.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            IReadOnlyList<ObjectPacket> objects = await _objectService.GetObjectsAsync();

            Objects.Clear();
            foreach (ObjectPacket obj in objects)
            {
                Objects.Add(obj);
            }

            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load objects");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void Add()
    {
        ObjectEditorViewModel vm = _editorFactory.Create();
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void Edit(ObjectPacket? obj)
    {
        if (obj is null)
        {
            return;
        }

        ObjectEditorViewModel vm = _editorFactory.Create(obj);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task KillAsync(ObjectPacket? obj)
    {
        if (obj is null)
        {
            return;
        }

        bool confirmed = await _confirmationDialogService.ShowConfirmationAsync(
            Strings.Get("KillObjectConfirmationTitle"),
            Strings.Format("KillObjectConfirmationMessage", obj.Name));

        if (!confirmed)
        {
            return;
        }

        // A retry must not keep showing the previous failure.
        ErrorMessage = string.Empty;

        try
        {
            await _objectService.KillAsync(obj.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            // The broadcast reason is a domain state, so the operator learns whether
            // to enable a port or free storage instead of reading a provider message.
            _logger?.LogError(ex, "Failed to kill object {ObjectName}", obj.Name);
            ErrorMessage = UserFacingFailure.Describe(ex);
        }
    }

    [RelayCommand]
    private void Close() => _navigationService.GoBack();

    partial void OnObjectsChanged(ObservableCollection<ObjectPacket> value)
    {
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        HasObjects = Objects.Count > 0;
        IsObjectsEmpty = !HasObjects;
    }
}
