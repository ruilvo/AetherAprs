// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Objects;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the APRS objects management page. Lists known objects (live
/// and killed), opens the object editor for add/edit, and kills objects.
/// </summary>
public sealed partial class ObjectsViewModel : ViewModelBase
{
    private readonly IObjectService _objectService;
    private readonly INavigationService _navigationService;
    private readonly IObjectEditorViewModelFactory _editorFactory;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILogger<ObjectsViewModel>? _logger;

    [ObservableProperty]
    public partial ObservableCollection<ObjectPacket> Objects { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    public ObjectsViewModel(
        IObjectService objectService,
        INavigationService navigationService,
        IObjectEditorViewModelFactory editorFactory,
        IConfirmationDialogService confirmationDialogService,
        ILogger<ObjectsViewModel>? logger = null)
    {
        _objectService = objectService ?? throw new ArgumentNullException(nameof(objectService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _editorFactory = editorFactory ?? throw new ArgumentNullException(nameof(editorFactory));
        _confirmationDialogService = confirmationDialogService ?? throw new ArgumentNullException(nameof(confirmationDialogService));
        _logger = logger;
    }

    /// <summary>
    /// Reloads the object list from the object service. Called by the page
    /// code-behind when the page is shown.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            var objects = await _objectService.GetObjectsAsync();

            Objects.Clear();
            foreach (var obj in objects)
            {
                Objects.Add(obj);
            }
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
        var vm = _editorFactory.Create();
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void Edit(ObjectPacket? obj)
    {
        if (obj is null)
        {
            return;
        }

        var vm = _editorFactory.Create(obj);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private async Task KillAsync(ObjectPacket? obj)
    {
        if (obj is null)
        {
            return;
        }

        var confirmed = await _confirmationDialogService.ShowConfirmationAsync(
            Strings.Get("KillObjectConfirmationTitle"),
            Strings.Format("KillObjectConfirmationMessage", obj.Name));

        if (!confirmed)
        {
            return;
        }

        try
        {
            await _objectService.KillAsync(obj.Name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to kill object {ObjectName}", obj.Name);
        }
    }

    [RelayCommand]
    private void Close()
    {
        _navigationService.GoBack();
    }
}
