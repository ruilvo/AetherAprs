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
        if (DataContext is AprsSymbolPickerViewModel viewModel)
        {
            // Sync property values to ViewModel
            viewModel.TableCharacter = TableCharacter;
            viewModel.CodeCharacter = CodeCharacter;
            viewModel.OverlayCharacter = OverlayCharacter;

            // Subscribe to ViewModel events
            viewModel.OpenSymbolSelectorRequested += OnOpenSymbolSelectorRequested;
            viewModel.OpenOverlaySelectorRequested += OnOpenOverlaySelectorRequested;

            // Subscribe to ViewModel property changes to sync back to control
            viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(AprsSymbolPickerViewModel.TableCharacter))
                {
                    TableCharacter = viewModel.TableCharacter;
                }
                else if (args.PropertyName == nameof(AprsSymbolPickerViewModel.CodeCharacter))
                {
                    CodeCharacter = viewModel.CodeCharacter;
                }
                else if (args.PropertyName == nameof(AprsSymbolPickerViewModel.OverlayCharacter))
                {
                    OverlayCharacter = viewModel.OverlayCharacter;
                }
            };
        }
    }

    private async void OnOpenSymbolSelectorRequested(object? sender, EventArgs e)
    {
        if (DataContext is not AprsSymbolPickerViewModel viewModel || viewModel.SymbolSelectorViewModel == null)
        {
            return;
        }

        var view = new SymbolSelectorView { DataContext = viewModel.SymbolSelectorViewModel };
        await DialogHost.Show(view, "RootDialogHost");
        viewModel.ApplyBaseSymbolSelection();
    }

    private async void OnOpenOverlaySelectorRequested(object? sender, EventArgs e)
    {
        if (DataContext is not AprsSymbolPickerViewModel viewModel || viewModel.OverlaySelectorViewModel == null)
        {
            return;
        }

        var view = new SymbolSelectorView { DataContext = viewModel.OverlaySelectorViewModel };
        await DialogHost.Show(view, "RootDialogHost");
        viewModel.ApplyOverlaySelection();
    }
}
