// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Localization;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the message template management page. Exposes the current
/// templates for display and raises Add/Edit requests for the page code-behind
/// to open the edit dialog; deletion goes through a confirmation dialog.
/// </summary>
public sealed partial class MessageTemplatesViewModel : ViewModelBase
{
    private readonly IMessageTemplateService _templateService;
    private readonly IConfirmationDialogService _confirmationDialogService;

    /// <summary>
    /// Gets the templates currently shown on the page.
    /// </summary>
    public ObservableCollection<MessageTemplate> Templates { get; } = [];

    /// <summary>
    /// Raised when the user requests adding a template.
    /// </summary>
    public event EventHandler<MessageTemplate?>? AddRequested;

    /// <summary>
    /// Raised when the user requests editing the given template.
    /// </summary>
    public event EventHandler<MessageTemplate>? EditRequested;

    public MessageTemplatesViewModel(
        IMessageTemplateService templateService,
        IConfirmationDialogService confirmationDialogService)
    {
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _confirmationDialogService = confirmationDialogService ?? throw new ArgumentNullException(nameof(confirmationDialogService));
    }

    /// <summary>
    /// Reloads the template list from the template service.
    /// </summary>
    public void Refresh()
    {
        Templates.Clear();
        foreach (var template in _templateService.Templates)
        {
            Templates.Add(template);
        }
    }

    [RelayCommand]
    private void Add()
    {
        AddRequested?.Invoke(this, null);
    }

    [RelayCommand]
    private void Edit(MessageTemplate template)
    {
        EditRequested?.Invoke(this, template);
    }

    [RelayCommand]
    private async Task DeleteAsync(MessageTemplate template)
    {
        var confirmed = await _confirmationDialogService.ShowConfirmationAsync(
            Strings.Get("DeleteTemplateConfirmationTitle"),
            Strings.Format("DeleteTemplateConfirmationMessage", template.Name));

        if (!confirmed)
        {
            return;
        }

        await _templateService.DeleteAsync(template.Name);
        Refresh();
    }

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
