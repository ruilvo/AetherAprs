// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;

namespace AetherAprs.ViewModels;

public partial class MessagesViewModel : ViewModelBase
{
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private readonly IServiceProvider _serviceProvider;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Messages");

    public ObservableCollection<ConversationThread> Conversations => _messageService.Conversations;

    public MessagesViewModel(
        IMessageService messageService,
        INavigationService navigationService,
        IServiceProvider serviceProvider)
    {
        _messageService = messageService;
        _navigationService = navigationService;
        _serviceProvider = serviceProvider;
    }

    [RelayCommand]
    private void NewMessage()
    {
        var vm = _serviceProvider.GetRequiredService<ConversationViewModel>();
        vm.InitializeNew();
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenConversation(ConversationThread? thread)
    {
        if (thread is null)
        {
            return;
        }

        var vm = _serviceProvider.GetRequiredService<ConversationViewModel>();
        vm.Initialize(thread.Peer);
        _navigationService.NavigateTo(vm);
    }
}
