// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.ViewModels.Components;
using AetherAprs.ViewModels.Pages;
using AetherAprs.Views.Components;
using Avalonia.Controls;
using Avalonia.Input;
using DialogHostAvalonia;
using System;
using System.Threading.Tasks;

namespace AetherAprs.Views.Pages;

public partial class MessageTemplatesPage : UserControl
{
    private MessageTemplatesViewModel? _currentViewModel;

    public MessageTemplatesPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MessageTemplatesViewModel viewModel)
        {
            if (viewModel.CloseCommand.CanExecute(null))
            {
                viewModel.CloseCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel to prevent memory leak
        if (_currentViewModel != null)
        {
            _currentViewModel.AddRequested -= OnAddRequested;
            _currentViewModel.EditRequested -= OnEditRequested;
        }

        _currentViewModel = DataContext as MessageTemplatesViewModel;

        if (_currentViewModel != null)
        {
            _currentViewModel.AddRequested += OnAddRequested;
            _currentViewModel.EditRequested += OnEditRequested;
            _currentViewModel.Refresh();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        // Unsubscribe when the page is torn down to prevent memory leak
        if (_currentViewModel != null)
        {
            _currentViewModel.AddRequested -= OnAddRequested;
            _currentViewModel.EditRequested -= OnEditRequested;
            _currentViewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private async void OnAddRequested(object? sender, MessageTemplate? template)
    {
        try
        {
            if (_currentViewModel == null)
            {
                return;
            }

            var editViewModel = _currentViewModel.CreateEditViewModel(null);
            await ShowEditDialogAsync(editViewModel, originalName: null);
        }
        catch (Exception)
        {
            // Swallow dialog failures: there is no logger in code-behind and an
            // unhandled exception in an async void handler would crash the app.
        }
    }

    private async void OnEditRequested(object? sender, MessageTemplate template)
    {
        try
        {
            if (_currentViewModel == null)
            {
                return;
            }

            // Capture the original name before the edit view model mutates the template
            var originalName = template.Name;
            var editViewModel = _currentViewModel.CreateEditViewModel(template);
            await ShowEditDialogAsync(editViewModel, originalName);
        }
        catch (Exception)
        {
            // Swallow dialog failures: there is no logger in code-behind and an
            // unhandled exception in an async void handler would crash the app.
        }
    }

    private async Task ShowEditDialogAsync(MessageTemplateEditViewModel editViewModel, string? originalName)
    {
        var result = await DialogHost.Show(
            new MessageTemplateEditDialogComponent { DataContext = editViewModel }, "MainDialogHost");

        if (_currentViewModel == null)
        {
            return;
        }

        if (result is not MessageTemplate templateResult)
        {
            return;
        }

        if (originalName is null)
        {
            await _currentViewModel.AddTemplateAsync(templateResult);
        }
        else
        {
            await _currentViewModel.UpdateTemplateAsync(originalName, templateResult);
        }
    }
}
