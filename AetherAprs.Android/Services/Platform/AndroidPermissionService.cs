// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AetherAprs.Services.Platform;

using Android.Content;

using Microsoft.Extensions.Logging;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of permission service.
/// Waits for MainActivity to complete permission requests.
/// </summary>
public class AndroidPermissionService(ILogger<AndroidPermissionService> logger) : IPermissionService, IPermissionGrantReader, IPermissionGrantRechecker
{
    private static readonly TaskCompletionSource<bool> _permissionsCompleteTcs = new();

    // Android reports "should show rationale" as false both before the first
    // request and after a permanent denial, so the service remembers which kinds
    // it already asked about. Only a kind that was asked before and no longer
    // suggests a rationale is a permanent denial.
    private static readonly ConcurrentDictionary<PermissionKind, bool> _promptedKinds = new();

    private static readonly TimeSpan _permissionWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<AndroidPermissionService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Recheck outcomes are compared across resume cycles, so the record has to
    // outlive a single read.
    private readonly PermissionGrantTracker _grantTracker = new();

    /// <summary>
    /// Called by MainActivity when initial permission requests are complete.
    /// </summary>
    internal static void SignalPermissionsComplete() => _permissionsCompleteTcs.TrySetResult(true);

    private static async Task WaitForPermissionsAsync(CancellationToken cancellationToken)
    {
        // Wait for MainActivity to complete permission requests, with timeout
        Task timeoutTask = Task.Delay(_permissionWaitTimeout, cancellationToken);
        Task completedTask = await Task.WhenAny(_permissionsCompleteTcs.Task, timeoutTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (completedTask != _permissionsCompleteTcs.Task)
        {
            throw new TimeoutException("Timeout waiting for MainActivity permission requests");
        }
    }

    /// <inheritdoc/>
    public async Task<PermissionRequestResult> RequestAsync(
        PermissionKind kind,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await WaitForPermissionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Without the startup burst there is no reliable prompt host, so the
            // permission is treated as unavailable instead of silently denied.
            _logger.LogWarning(exception, "Permission check for {Permission} timed out", kind);
            return PermissionRequestResult.Unavailable(kind);
        }

        MainActivity? activity = MainActivity.Instance;
        if (activity == null || activity.IsDestroyed)
        {
            return PermissionRequestResult.Unavailable(kind);
        }

        if (!IsRequiredOnThisPlatform(kind))
        {
            return PermissionRequestResult.Granted(kind);
        }

        if (kind == PermissionKind.BatteryOptimization)
        {
            return RequestBatteryOptimizationExemption(activity);
        }

        bool requestedBefore = _promptedKinds.ContainsKey(kind);
        PermissionPromptResult prompt = await PromptAsync(activity, kind).ConfigureAwait(false);
        if (prompt == PermissionPromptResult.Abandoned)
        {
            // No dialog answered: a teardown, an activity that is gone, or the
            // operator walking away. Recording a denial would be wrong.
            return PermissionRequestResult.Unavailable(kind);
        }

        bool granted = prompt == PermissionPromptResult.Granted;

        // The permission was asked about either way; a later read without a
        // rationale and without a grant is then a permanent denial, not a first
        // denial the operator can be asked about again.
        _promptedKinds[kind] = true;

        return PermissionRequestPolicy.Evaluate(
            kind,
            granted,
            HasRationale(activity, kind),
            requestedBefore);
    }

    /// <inheritdoc/>
    public Task<bool> OpenApplicationSettingsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MainActivity? activity = MainActivity.Instance;
        if (activity == null || activity.IsDestroyed)
        {
            return Task.FromResult(false);
        }

        try
        {
            Intent intent = new(global::Android.Provider.Settings.ActionApplicationDetailsSettings);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            return Task.FromResult(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open application settings");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public PermissionGrant Read(PermissionKind kind)
    {
        MainActivity? activity = MainActivity.Instance;
        if (activity == null || activity.IsDestroyed)
        {
            // No host means no honest answer; guessing "denied" would record an
            // operator choice that never happened.
            return PermissionGrant.Unsupported;
        }

        try
        {
            if (!IsRequiredOnThisPlatform(kind))
            {
                return PermissionGrant.Granted;
            }

            if (kind == PermissionKind.BatteryOptimization)
            {
                if (activity.GetSystemService(global::Android.Content.Context.PowerService) is not global::Android.OS.PowerManager powerManager)
                {
                    return PermissionGrant.Unsupported;
                }

                return powerManager.IsIgnoringBatteryOptimizations(activity.PackageName!)
                    ? PermissionGrant.Granted
                    : PermissionGrant.Denied;
            }

            bool granted = kind switch
            {
                PermissionKind.Notifications => IsGranted(activity, global::Android.Manifest.Permission.PostNotifications),
                PermissionKind.Bluetooth =>
                    IsGranted(activity, global::Android.Manifest.Permission.BluetoothScan) &&
                    IsGranted(activity, global::Android.Manifest.Permission.BluetoothConnect),
                PermissionKind.Location =>
                    IsGranted(activity, global::Android.Manifest.Permission.AccessFineLocation) ||
                    IsGranted(activity, global::Android.Manifest.Permission.AccessCoarseLocation),
                PermissionKind.BackgroundLocation =>
                    IsGranted(activity, global::Android.Manifest.Permission.AccessBackgroundLocation),
                PermissionKind.Audio => IsGranted(activity, global::Android.Manifest.Permission.RecordAudio),
                _ => false
            };

            return granted ? PermissionGrant.Granted : PermissionGrant.Denied;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not read the current {Permission} grant", kind);
            return PermissionGrant.Unsupported;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<PermissionGrantTransition> Recheck()
    {
        MainActivity? activity = MainActivity.Instance;
        if (activity == null || activity.IsDestroyed)
        {
            return [];
        }

        List<PermissionGrantTransition> transitions = [];
        foreach (PermissionKind kind in Enum.GetValues<PermissionKind>())
        {
            transitions.Add(_grantTracker.Observe(
                kind,
                Read(kind),
                HasRationale(activity, kind),
                _promptedKinds.ContainsKey(kind)));
        }

        return transitions;
    }

    private static bool IsGranted(MainActivity activity, string permission) =>
        activity.CheckSelfPermission(permission) == global::Android.Content.PM.Permission.Granted;

    private static bool IsRequiredOnThisPlatform(PermissionKind kind) => kind switch
    {
        // Both permissions only exist as runtime permissions from these levels; the
        // older levels declare them in the manifest, so nothing has to be prompt.
        PermissionKind.Notifications => global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu,
        PermissionKind.Bluetooth => global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.S,
        _ => true
    };

    private static Task<PermissionPromptResult> PromptAsync(
        MainActivity activity,
        PermissionKind kind) => kind switch
        {
            PermissionKind.Notifications => activity.RequestNotificationPermissionAsync(),
            PermissionKind.Bluetooth => activity.RequestBluetoothPermissionAsync(),
            PermissionKind.Location => RequestLocationAsync(activity),
            PermissionKind.BackgroundLocation => RequestBackgroundLocationAsync(activity),
            PermissionKind.Audio => activity.RequestAudioPermissionAsync(),
            _ => Task.FromResult(PermissionPromptResult.Abandoned),
        };

    private static async Task<PermissionPromptResult> RequestBackgroundLocationAsync(MainActivity activity)
    {
        // Background location keeps its own request code: the system answers it
        // through a settings page on Android 11 and later, outside our queue.
        bool granted = await activity.RequestBackgroundLocationPermissionAsync().ConfigureAwait(false);
        return granted ? PermissionPromptResult.Granted : PermissionPromptResult.Denied;
    }

    private static async Task<PermissionPromptResult> RequestLocationAsync(MainActivity activity)
    {
        // Location has its own prompt pair and result codes, so it keeps its
        // existing path instead of joining the queue.
        bool granted = await activity.RequestLocationPermissionsAsync().ConfigureAwait(false);
        return granted ? PermissionPromptResult.Granted : PermissionPromptResult.Denied;
    }

    private static bool HasRationale(MainActivity activity, PermissionKind kind) => kind switch
    {
        PermissionKind.Notifications =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.PostNotifications),
        PermissionKind.Bluetooth =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothScan) ||
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.BluetoothConnect),
        PermissionKind.Location =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessFineLocation) ||
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.AccessCoarseLocation),
        PermissionKind.Audio =>
            activity.ShouldShowRequestPermissionRationale(global::Android.Manifest.Permission.RecordAudio),
        _ => false
    };

    private PermissionRequestResult RequestBatteryOptimizationExemption(MainActivity activity)
    {
        if (activity.GetSystemService(global::Android.Content.Context.PowerService) is not global::Android.OS.PowerManager powerManager)
        {
            return PermissionRequestResult.Unavailable(PermissionKind.BatteryOptimization);
        }

        if (powerManager.IsIgnoringBatteryOptimizations(activity.PackageName!))
        {
            return PermissionRequestResult.Granted(PermissionKind.BatteryOptimization);
        }

        try
        {
            // The system dialog result is asynchronous and outside our control, so a
            // plain denial reports "prompt shown" and the caller may ask again.
            Intent intent = new(global::Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
            intent.SetData(global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            activity.StartActivity(intent);
            return PermissionRequestResult.Denied(PermissionKind.BatteryOptimization);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open battery optimization settings");
            return PermissionRequestResult.Unavailable(PermissionKind.BatteryOptimization);
        }
    }
}
