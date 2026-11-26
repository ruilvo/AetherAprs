// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Localization;
using AetherAprs.Models.Aprs.Packets;
using AetherAprs.Services.Messaging;
using AetherAprs.Services.UI;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Pages;

/// <summary>
/// View model for the APRS bulletins page. Lists received/sent bulletins and
/// composes new ones via <see cref="IBulletinService"/>.
/// </summary>
public sealed partial class BulletinsViewModel(
    IBulletinService bulletinService,
    INavigationService navigationService,
    ILogger<BulletinsViewModel>? logger = null) : ViewModelBase
{
    /// <summary>APRS bulletin name limit per the APRS specification.</summary>
    private const int BulletinNameMaxLength = 9;

    /// <summary>APRS bulletin text limit per the APRS specification.</summary>
    private const int BulletinTextMaxLength = 67;

    private readonly IBulletinService _bulletinService = bulletinService ?? throw new ArgumentNullException(nameof(bulletinService));
    private readonly INavigationService _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    private readonly ILogger<BulletinsViewModel>? _logger = logger;

    [ObservableProperty]
    public partial ObservableCollection<BulletinPacket> Bulletins { get; set; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Bulletins"/> has any items.
    /// Updated whenever the collection is mutated so the page bindings do not
    /// go stale on in-place changes.
    /// </summary>
    [ObservableProperty]
    public partial bool HasBulletins { get; set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Bulletins"/> is empty.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBulletinsEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    /// <summary>
    /// Reloads the bulletin list. Called by the page code-behind when the page
    /// is shown.
    /// </summary>
    public async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;
            IReadOnlyList<BulletinPacket> bulletins = await _bulletinService.GetBulletinsAsync();

            Bulletins.Clear();
            foreach (BulletinPacket bulletin in bulletins)
            {
                Bulletins.Add(bulletin);
            }

            UpdateEmptyState();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load bulletins");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSend() => !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        ErrorMessage = null;
        string? name = Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ErrorMessage = Strings.Get("ValidationBulletinNameRequired");
            return;
        }

        if (!name.StartsWith("BLN", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = Strings.Get("ValidationBulletinNamePrefix");
            return;
        }

        if (name.Length > BulletinNameMaxLength)
        {
            ErrorMessage = Strings.Get("ValidationBulletinNameMaxLength");
            return;
        }

        string? text = Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            ErrorMessage = Strings.Get("ValidationBulletinTextRequired");
            return;
        }

        if (text.Length > BulletinTextMaxLength)
        {
            ErrorMessage = Strings.Get("ValidationBulletinTextMaxLength");
            return;
        }

        IsSending = true;
        try
        {
            await _bulletinService.SendAsync(name, text);
            Text = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to send bulletin {BulletinName}", name);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSending = false;
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Close() => _navigationService.GoBack();

    partial void OnBulletinsChanged(ObservableCollection<BulletinPacket> value)
    {
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        HasBulletins = Bulletins.Count > 0;
        IsBulletinsEmpty = !HasBulletins;
    }
}
