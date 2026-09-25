// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AetherAprs.ViewModels;

public partial class MessagesViewModel(
    IMessageService messageService,
    INavigationService navigationService,
    IConversationViewModelFactory conversationFactory) : ViewModelBase
{
    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Messages");

    public ObservableCollection<ConversationThread> Conversations => messageService.Conversations;

    [RelayCommand]
    private void NewMessage()
    {
        var vm = conversationFactory.CreateNew();
        navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenConversation(ConversationThread? thread)
    {
        if (thread is null)
        {
            return;
        }

        var vm = conversationFactory.Create(thread.Peer);
        navigationService.NavigateTo(vm);
    }
}
