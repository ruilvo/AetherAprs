// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AetherAprs.Models.Aprs;
using AetherAprs.Models.Messaging;
using AetherAprs.Services;
using AetherAprs.ViewModels;

namespace AetherAprs.ViewModels.Pages;

public partial class ConversationViewModel : ViewModelBase
{
    private readonly IMessageService _messageService;
    private readonly INavigationService _navigationService;
    private ConversationThread? _thread;
    private bool _isNewConversation;

    [ObservableProperty]
    public partial string Title { get; set; } = "New Message";

    [ObservableProperty]
    public partial string DestinationCallsign { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MessageText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsDestinationEditable { get; set; } = true;

    public ObservableCollection<StoredMessage> Messages { get; } = new();

    public ConversationViewModel(IMessageService messageService, INavigationService navigationService)
    {
        _messageService = messageService;
        _navigationService = navigationService;
    }

    public void InitializeNew()
    {
        _isNewConversation = true;
        _thread = null;
        Title = "New Message";
        DestinationCallsign = string.Empty;
        MessageText = string.Empty;
        ErrorMessage = null;
        IsDestinationEditable = true;
        Messages.Clear();
    }

    public void Initialize(Callsign peer)
    {
        _isNewConversation = false;
        _thread = _messageService.GetOrCreateConversation(peer);
        Title = peer.ToString();
        DestinationCallsign = peer.ToString();
        MessageText = string.Empty;
        ErrorMessage = null;
        IsDestinationEditable = false;
        Messages.Clear();
        foreach (var message in _thread.Messages)
        {
            Messages.Add(message);
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        ErrorMessage = null;

        if (!Callsign.TryParse(DestinationCallsign, out var addressee))
        {
            ErrorMessage = "Enter a valid destination callsign (e.g. N0CALL or N0CALL-1).";
            return;
        }

        if (string.IsNullOrWhiteSpace(MessageText))
        {
            ErrorMessage = "Message text is required.";
            return;
        }

        try
        {
            await _messageService.SendAsync(addressee, MessageText);
            _thread = _messageService.GetOrCreateConversation(addressee);
            if (_isNewConversation)
            {
                _isNewConversation = false;
                IsDestinationEditable = false;
                Title = addressee.ToString();
                DestinationCallsign = addressee.ToString();
            }

            Messages.Clear();
            foreach (var message in _thread.Messages)
            {
                Messages.Add(message);
            }

            MessageText = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
