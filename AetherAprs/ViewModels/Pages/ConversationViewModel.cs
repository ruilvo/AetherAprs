// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.Localization;
using AetherAprs.ViewModels;
using Microsoft.Extensions.Logging;

namespace AetherAprs.ViewModels.Pages;

public partial class ConversationViewModel : ViewModelBase
{
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<ConversationViewModel> _logger;
    private ConversationThread? _thread;
    private bool _isNewConversation;

    [ObservableProperty]
    public partial string Title { get; set; } = Strings.Get("NewMessage");

    [ObservableProperty]
    public partial string DestinationCallsign { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MessageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsDestinationEditable { get; set; } = true;

    public ObservableCollection<StoredMessage> Messages { get; } = new();

    public ConversationViewModel(
        IMessageService messageService,
        INavigationService navigationService,
        ILogger<ConversationViewModel> logger)
    {
        _messageService = messageService;
        _navigationService = navigationService;
        _logger = logger;
    }

    public void InitializeNew()
    {
        DetachThread();
        _isNewConversation = true;
        _thread = null;
        Title = Strings.Get("NewMessage");
        DestinationCallsign = string.Empty;
        MessageText = string.Empty;
        ErrorMessage = null;
        IsDestinationEditable = true;
        Messages.Clear();
    }

    public void Initialize(Callsign peer)
    {
        _isNewConversation = false;
        Title = peer.ToString();
        DestinationCallsign = peer.ToString();
        MessageText = string.Empty;
        ErrorMessage = null;
        IsDestinationEditable = false;
        AttachThread(_messageService.GetOrCreateConversation(peer));
    }

    [RelayCommand]
    private void GoBack()
    {
        DetachThread();
        _navigationService.GoBack();
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        ErrorMessage = null;

        if (!Callsign.TryParse(DestinationCallsign, out var addressee))
        {
            ErrorMessage = Strings.Get("InvalidDestinationCallsign");
            return;
        }

        if (string.IsNullOrWhiteSpace(MessageText))
        {
            ErrorMessage = Strings.Get("MessageTextRequired");
            return;
        }

        try
        {
            await _messageService.SendAsync(addressee, MessageText);
            _logger.LogInformation("Message sent to {Addressee}", addressee);
            var thread = _messageService.GetOrCreateConversation(addressee);
            if (_isNewConversation)
            {
                _isNewConversation = false;
                IsDestinationEditable = false;
                Title = addressee.ToString();
                DestinationCallsign = addressee.ToString();
                AttachThread(thread);
            }
            else if (!ReferenceEquals(_thread, thread))
            {
                AttachThread(thread);
            }
            else
            {
                SyncMessages();
            }

            MessageText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send message to {Addressee}", DestinationCallsign);
            ErrorMessage = ex.Message;
        }
    }

    private void AttachThread(ConversationThread thread)
    {
        DetachThread();
        _thread = thread;
        _thread.Messages.CollectionChanged += OnThreadMessagesChanged;
        SyncMessages();
    }

    private void DetachThread()
    {
        if (_thread != null)
        {
            _thread.Messages.CollectionChanged -= OnThreadMessagesChanged;
        }
    }

    private void OnThreadMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        SyncMessages();

    private void SyncMessages()
    {
        Messages.Clear();
        if (_thread == null)
            return;

        foreach (var message in _thread.Messages)
        {
            Messages.Add(message);
        }
    }
}
