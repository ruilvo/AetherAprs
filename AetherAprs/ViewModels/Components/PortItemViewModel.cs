// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration;
using AetherAprs.Services.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AetherAprs.ViewModels.Components;

public partial class PortItemViewModel : ViewModelBase
{
    private readonly PortConfig _config;
    private readonly Func<PortItemViewModel, Task> _onToggle;
    private readonly Func<PortItemViewModel, Task> _onShowOnMapToggle;
    private readonly Func<PortItemViewModel, Task> _onDelete;
    private readonly Action<PortItemViewModel> _onEdit;
    private readonly ILogger<PortItemViewModel>? _logger;
    private bool _isInitializing;
    private PortSessionStatus? _sessionStatus;

    public Guid Id => _config.Id;

    public string Name => _config.Name;

    public string TypeName => _config.TypeSettings switch
    {
        AprsIsSettings => Localization.Strings.Get("PortTypeAprsIs"),
        KissSettings => Localization.Strings.Get("PortTypeKiss"),
        AfskSettings => Localization.Strings.Get("PortTypeAfsk"),
        _ => Localization.Strings.Get("PortTypeUnknown")
    };

    /// <summary>
    /// What the port is doing right now. It combines the operator's intent with the
    /// runtime session state, so a port that is enabled but still reconnecting reads
    /// as connecting rather than as running.
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!IsEnabled)
            {
                return Localization.Strings.Get("Stopped");
            }

            if (_sessionStatus is not { } status)
            {
                // No session has been reported yet (the coordinator may not have run);
                // the intent is all that is known.
                return Localization.Strings.Get("Running");
            }

            return status.State switch
            {
                PortSessionState.Connected => Localization.Strings.Get("PortStatusConnected"),
                PortSessionState.Connecting => Localization.Strings.Get("PortStatusConnecting"),
                PortSessionState.Degraded => Localization.Strings.Get("PortStatusDegraded"),
                _ when status.NextRetryUtc is not null => Localization.Strings.Format(
                    "PortStatusWaitingToRetry",
                    Math.Max(status.RetryCount, 1)),
                _ => Localization.Strings.Get("PortStatusStarting")
            };
        }
    }

    /// <summary>Consecutive failed connect attempts reported by the session.</summary>
    public int RetryCount => _sessionStatus?.RetryCount ?? 0;

    /// <summary>When the session will next try to connect, if it is waiting.</summary>
    public DateTimeOffset? NextRetryUtc => _sessionStatus?.NextRetryUtc;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowOnMap { get; set; }

    /// <summary>
    /// Applies the runtime session status for this port and refreshes the rendered
    /// text. Passing <see langword="null"/> means the session has no status yet.
    /// </summary>
    public void ApplySessionStatus(PortSessionStatus? status)
    {
        _sessionStatus = status;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RetryCount));
        OnPropertyChanged(nameof(NextRetryUtc));
    }

    public PortItemViewModel(
        PortConfig config,
        Func<PortItemViewModel, Task> onToggle,
        Func<PortItemViewModel, Task> onShowOnMapToggle,
        Func<PortItemViewModel, Task> onDelete,
        Action<PortItemViewModel> onEdit,
        ILogger<PortItemViewModel>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _onToggle = onToggle;
        _onShowOnMapToggle = onShowOnMapToggle;
        _onDelete = onDelete;
        _onEdit = onEdit;
        _logger = logger;

        _isInitializing = true;
        IsEnabled = config.IsEnabled;
        ShowOnMap = config.ShowOnMap;
        _isInitializing = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        if (!_isInitializing)
        {
            _ = HandleToggleAsync();
        }
    }

    partial void OnShowOnMapChanged(bool value)
    {
        if (!_isInitializing)
        {
            _ = HandleShowOnMapToggleAsync();
        }
    }

    private async Task HandleToggleAsync()
    {
        try
        {
            await _onToggle(this);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error toggling port {PortName} (ID: {PortId})", Name, Id);
            // Revert the toggle on failure
            _isInitializing = true;
            IsEnabled = !IsEnabled;
            _isInitializing = false;
        }
    }

    private async Task HandleShowOnMapToggleAsync()
    {
        try
        {
            await _onShowOnMapToggle(this);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error toggling ShowOnMap for port {PortName} (ID: {PortId})", Name, Id);
            // Revert on failure
            _isInitializing = true;
            ShowOnMap = !ShowOnMap;
            _isInitializing = false;
        }
    }

    [RelayCommand]
    private void Edit() => _onEdit(this);

    [RelayCommand]
    private async Task DeleteAsync() => await _onDelete(this);

    [RelayCommand]
    private void ToggleShowOnMap() => ShowOnMap = !ShowOnMap;

    public PortConfig BuildConfig()
    {
        // Return a copy: the item's config is a snapshot of service state and
        // must not be mutated as a side effect of opening the edit form.
        PortConfig config = _config.Clone();
        config.IsEnabled = IsEnabled;
        config.ShowOnMap = ShowOnMap;
        return config;
    }
}
