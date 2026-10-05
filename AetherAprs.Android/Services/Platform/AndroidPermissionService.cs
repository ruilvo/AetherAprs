// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Platform;
using System;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android implementation of permission service.
/// Waits for MainActivity to complete permission requests.
/// </summary>
public class AndroidPermissionService : IPermissionService
{
    private static readonly TaskCompletionSource<bool> _permissionsCompleteTcs = new();

    /// <summary>
    /// Called by MainActivity when initial permission requests are complete.
    /// </summary>
    internal static void SignalPermissionsComplete()
    {
        _permissionsCompleteTcs.TrySetResult(true);
    }

    private async Task WaitForPermissionsAsync()
    {
        // Wait for MainActivity to complete permission requests, with timeout
        var timeoutTask = Task.Delay(30000);
        var completedTask = await Task.WhenAny(_permissionsCompleteTcs.Task, timeoutTask);
        
        if (completedTask == timeoutTask)
        {
            throw new TimeoutException("Timeout waiting for MainActivity permission requests");
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RequestNotificationPermissionAsync()
    {
        await WaitForPermissionsAsync();
        
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            return false;
        }

        if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.Tiramisu)
        {
            return true;
        }

        var granted = activity.CheckSelfPermission(global::Android.Manifest.Permission.PostNotifications) 
            == global::Android.Content.PM.Permission.Granted;
        return granted;
    }

    /// <inheritdoc/>
    public async Task<bool> RequestBluetoothPermissionAsync()
    {
        await WaitForPermissionsAsync();
        
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            return false;
        }

        if (global::Android.OS.Build.VERSION.SdkInt < global::Android.OS.BuildVersionCodes.S)
        {
            return true;
        }

        var scanGranted = activity.CheckSelfPermission(global::Android.Manifest.Permission.BluetoothScan) 
            == global::Android.Content.PM.Permission.Granted;
        var connectGranted = activity.CheckSelfPermission(global::Android.Manifest.Permission.BluetoothConnect) 
            == global::Android.Content.PM.Permission.Granted;
        return scanGranted && connectGranted;
    }

    /// <inheritdoc/>
    public async Task<bool> RequestLocationPermissionAsync()
    {
        await WaitForPermissionsAsync();
        
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            return false;
        }

        var fineGranted = activity.CheckSelfPermission(global::Android.Manifest.Permission.AccessFineLocation) 
            == global::Android.Content.PM.Permission.Granted;
        var coarseGranted = activity.CheckSelfPermission(global::Android.Manifest.Permission.AccessCoarseLocation) 
            == global::Android.Content.PM.Permission.Granted;
        return fineGranted || coarseGranted;
    }
}
