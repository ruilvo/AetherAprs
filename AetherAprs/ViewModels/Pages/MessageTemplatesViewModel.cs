// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Factories.ViewModels;
using AetherAprs.Localization;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the message template management page. Exposes the current
/// templates for display, raises Add/Edit requests for the page code-behind
/// to open the edit dialog, creates the edit dialog view model and closes the
/// page via the navigation service.
/// </summary>
public sealed partial class MessageTemplatesViewModel(
    IMessageTemplateService templateService,
    IConfirmationDialogService confirmationDialogService,
    INavigationService navigationService,
    IMessageTemplateEditViewModelFactory editViewModelFactory) : ViewModelBase
{
    private readonly IMessageTemplateService _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
    private readonly IConfirmationDialogService _confirmationDialogService = confirmationDialogService ?? throw new ArgumentNullException(nameof(confirmationDialogService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly IMessageTemplateEditViewModelFactory _editViewModelFactory = editViewModelFactory ?? throw new ArgumentNullException(nameof(editViewModelFactory));

    /// <summary>
    /// Gets the templates currently shown on the page.
    /// </summary>
    public ObservableCollection<MessageTemplate> Templates { get; } = [];

    /// <summary>
    /// Gets a value indicating whether <see cref="Templates"/> has any items.
    /// Updated whenever the collection is refreshed so the page bindings do not
    /// go stale on in-place changes.
    /// </summary>
    [ObservableProperty]
    public partial bool HasTemplates { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Templates"/> is empty.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTemplatesEmpty { get; set; } = true;

    /// <summary>
    /// Raised when the user requests adding a template.
    /// </summary>
    public event EventHandler<MessageTemplate?>? AddRequested;

    /// <summary>
    /// Raised when the user requests editing the given template.
    /// </summary>
    public event EventHandler<MessageTemplate>? EditRequested;

    /// <summary>
    /// Reloads the template list from the template service.
    /// </summary>
    public void Refresh()
    {
        Templates.Clear();
        foreach (MessageTemplate template in _templateService.Templates)
        {
            Templates.Add(template);
        }

        HasTemplates = Templates.Count > 0;
        IsTemplatesEmpty = !HasTemplates;
    }

    [RelayCommand]
    private void Add() => AddRequested?.Invoke(this, null);

    /// <summary>
    /// Creates the edit dialog view model for adding (null original) or
    /// editing the given template, passing the current template names for
    /// duplicate detection (excluding the original being edited).
    /// </summary>
    public MessageTemplateEditViewModel CreateEditViewModel(MessageTemplate? original)
    {
        string? originalName = original?.Name;
        List<string> existingNames = [];
        foreach (MessageTemplate template in _templateService.Templates)
        {
            if (!string.Equals(template.Name, originalName, StringComparison.OrdinalIgnoreCase))
            {
                existingNames.Add(template.Name);
            }
        }

        return _editViewModelFactory.Create(original, existingNames);
    }

    [RelayCommand]
    private void Edit(MessageTemplate template) => EditRequested?.Invoke(this, template);

    [RelayCommand]
    private async Task DeleteAsync(MessageTemplate template)
    {
        bool confirmed = await _confirmationDialogService.ShowConfirmationAsync(
            Strings.Get("DeleteTemplateConfirmationTitle"),
            Strings.Format("DeleteTemplateConfirmationMessage", template.Name));

        if (!confirmed)
        {
            return;
        }

        await _templateService.DeleteAsync(template.Name);
        Refresh();
    }

    [RelayCommand]
    private void Close() => _navigationService.GoBack();

    /// <summary>
    /// Persists a newly created template and refreshes the list.
    /// Called by the page code-behind after the edit dialog closes.
    /// </summary>
    public async Task AddTemplateAsync(MessageTemplate template)
    {
        await _templateService.AddAsync(template);
        Refresh();
    }

    /// <summary>
    /// Persists changes to the template originally named
    /// <paramref name="originalName"/> and refreshes the list. Called by the
    /// page code-behind after the edit dialog closes.
    /// </summary>
    public async Task UpdateTemplateAsync(string originalName, MessageTemplate template)
    {
        await _templateService.UpdateAsync(originalName, template);
        Refresh();
    }
}
