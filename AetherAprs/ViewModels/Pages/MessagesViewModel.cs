// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

using AetherAprs.Factories.ViewModels;
using AetherAprs.Models.Messaging;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherAprs.ViewModels.Pages;

public partial class MessagesViewModel : ViewModelBase, IDisposable
{
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private readonly IConversationViewModelFactory _conversationFactory;
    private readonly BulletinsViewModel _bulletinsViewModel;

    [ObservableProperty]
    public partial string Title { get; set; } = Localization.Strings.Get("Messages");

    public ObservableCollection<ConversationThread> Conversations { get; } = [];

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
        _messageService.ConversationsChanged += OnServiceConversationsChanged;
        SyncConversations();
    }

    private void OnConversationsChanged(object? sender, NotifyCollectionChangedEventArgs e) => OnPropertyChanged(nameof(HasConversations));

    private void OnServiceConversationsChanged(object? sender, EventArgs e) => SyncConversations();

    /// <summary>
    /// Merges the service's immutable snapshot into the owned collection, so the
    /// selected item and object identity survive reordering.
    /// </summary>
    private void SyncConversations()
    {
        IReadOnlyList<ConversationThread> snapshot = _messageService.Conversations;
        HashSet<ConversationThread> live = [.. snapshot];

        for (int index = Conversations.Count - 1; index >= 0; index--)
        {
            if (!live.Contains(Conversations[index]))
            {
                Conversations.RemoveAt(index);
            }
        }

        for (int index = 0; index < snapshot.Count; index++)
        {
            ConversationThread thread = snapshot[index];
            int current = Conversations.IndexOf(thread);
            if (current < 0)
            {
                Conversations.Insert(index, thread);
            }
            else if (current != index)
            {
                Conversations.Move(current, index);
            }
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Conversations.CollectionChanged -= OnConversationsChanged;
        _messageService.ConversationsChanged -= OnServiceConversationsChanged;
    }

    [RelayCommand]
    private void NewMessage()
    {
        ConversationViewModel vm = _conversationFactory.CreateNew();
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenConversation(ConversationThread? thread)
    {
        if (thread is null)
        {
            return;
        }

        ConversationViewModel vm = _conversationFactory.Create(thread.Peer);
        _navigationService.NavigateTo(vm);
    }

    [RelayCommand]
    private void OpenBulletins() => _navigationService.NavigateTo(_bulletinsViewModel);
}
