// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Threading.Tasks;
using AetherAprs.ViewModels.Components;
using Avalonia;
using Avalonia.Controls;
using DialogHostAvalonia;

namespace AetherAprs.Views.Components;

public partial class AprsSymbolPickerView : UserControl
{
    public static readonly StyledProperty<string> TableCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string>(nameof(TableCharacter), "/");

    public static readonly StyledProperty<string> CodeCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string>(nameof(CodeCharacter), "[");

    public static readonly StyledProperty<string?> OverlayCharacterProperty =
        AvaloniaProperty.Register<AprsSymbolPickerView, string?>(nameof(OverlayCharacter));

    private AprsSymbolPickerViewModel? _currentViewModel;

    public string TableCharacter
    {
        get => GetValue(TableCharacterProperty);
        set => SetValue(TableCharacterProperty, value);
    }

    public string CodeCharacter
    {
        get => GetValue(CodeCharacterProperty);
        set => SetValue(CodeCharacterProperty, value);
    }

    public string? OverlayCharacter
    {
        get => GetValue(OverlayCharacterProperty);
        set => SetValue(OverlayCharacterProperty, value);
    }

    public AprsSymbolPickerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old ViewModel to prevent memory leak
        if (_currentViewModel != null)
        {
            _currentViewModel.OpenSymbolSelectorRequested -= OnOpenSymbolSelectorRequested;
            _currentViewModel.OpenOverlaySelectorRequested -= OnOpenOverlaySelectorRequested;
            _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (DataContext is AprsSymbolPickerViewModel viewModel)
        {
            _currentViewModel = viewModel;

            // Sync property values to ViewModel
            viewModel.TableCharacter = TableCharacter;
            viewModel.CodeCharacter = CodeCharacter;
            viewModel.OverlayCharacter = OverlayCharacter;

            // Subscribe to ViewModel events
            viewModel.OpenSymbolSelectorRequested += OnOpenSymbolSelectorRequested;
            viewModel.OpenOverlaySelectorRequested += OnOpenOverlaySelectorRequested;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        else
        {
            _currentViewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (_currentViewModel == null)
        {
            return;
        }

        if (args.PropertyName == nameof(AprsSymbolPickerViewModel.TableCharacter))
        {
            TableCharacter = _currentViewModel.TableCharacter;
        }
        else if (args.PropertyName == nameof(AprsSymbolPickerViewModel.CodeCharacter))
        {
            CodeCharacter = _currentViewModel.CodeCharacter;
        }
        else if (args.PropertyName == nameof(AprsSymbolPickerViewModel.OverlayCharacter))
        {
            OverlayCharacter = _currentViewModel.OverlayCharacter;
        }
    }

    private async void OnOpenSymbolSelectorRequested(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not AprsSymbolPickerViewModel viewModel || viewModel.SymbolSelectorViewModel == null)
            {
                return;
            }

            var selectorViewModel = viewModel.SymbolSelectorViewModel;
            var view = new SymbolSelectorView { DataContext = selectorViewModel };
            
            // Subscribe to symbol selected event to close dialog
            void OnSymbolSelected(object? s, EventArgs args)
            {
                DialogHost.Close("MainDialogHost");
            }
            
            selectorViewModel.SymbolSelected += OnSymbolSelected;
            
            try
            {
                await DialogHost.Show(view, "MainDialogHost");
                // Apply selection after dialog closes (whether by selection or back button)
                if (selectorViewModel.IsSelected)
                {
                    viewModel.ApplyBaseSymbolSelection();
                }
            }
            finally
            {
                selectorViewModel.SymbolSelected -= OnSymbolSelected;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show symbol selector: {ex}");
        }
    }

    private async void OnOpenOverlaySelectorRequested(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not AprsSymbolPickerViewModel viewModel || viewModel.OverlaySelectorViewModel == null)
            {
                return;
            }

            var selectorViewModel = viewModel.OverlaySelectorViewModel;
            var view = new SymbolSelectorView { DataContext = selectorViewModel };
            
            // Subscribe to symbol selected event to close dialog
            void OnSymbolSelected(object? s, EventArgs args)
            {
                DialogHost.Close("MainDialogHost");
            }
            
            selectorViewModel.SymbolSelected += OnSymbolSelected;
            
            try
            {
                await DialogHost.Show(view, "MainDialogHost");
                // Apply selection after dialog closes (whether by selection or back button)
                if (selectorViewModel.IsSelected)
                {
                    viewModel.ApplyOverlaySelection();
                }
            }
            finally
            {
                selectorViewModel.SymbolSelected -= OnSymbolSelected;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show overlay selector: {ex}");
        }
    }
}
