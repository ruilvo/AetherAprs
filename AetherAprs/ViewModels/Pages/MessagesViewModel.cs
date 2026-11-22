// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Pages;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace AetherAprs.ViewModels.Pages;

public partial class MessagesViewModel : ViewModelBase, IDisposable
{
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private readonly IConversationViewModelFactory _conversationFactory;
    private readonly BulletinsViewModel _bulletinsViewModel;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Messages");

    public ObservableCollection<ConversationThread> Conversations => _messageService.Conversations;
    public bool HasConversations => Conversations.Count > 0;

    public MessagesViewModel(
        IMessageService messageService,
        INavigationService navigationService,
        IConversationViewModelFactory conversationFactory,
        BulletinsViewModel bulletinsViewModel)
    {
        _messageService = messageService;
        _navigationService = navigationService;
        _conversationFactory = conversationFactory;
        _bulletinsViewModel = bulletinsViewModel;
        Conversations.CollectionChanged += OnConversationsChanged;
    }

    private void OnConversationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasConversations));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Conversations.CollectionChanged -= OnConversationsChanged;
    }

    [RelayCommand]
    private void NewMessage()
    {
        var vm = _conversationFactory.CreateNew();
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenConversation(ConversationThread? thread)
    {
        if (thread is null)
        {
            return;
        }

        var vm = _conversationFactory.Create(thread.Peer);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenBulletins()
    {
        _navigationService.NavigateTo(_bulletinsViewModel);
    }
}
