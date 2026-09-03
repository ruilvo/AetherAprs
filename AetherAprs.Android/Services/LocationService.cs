// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Android;
using Android.Content;
using Android.Content.PM;
using Android.Locations;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AetherAprs.Services;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services;

/// <summary>
/// Android-specific implementation of ILocationService.
/// Uses Android's LocationManager to retrieve device location.
/// </summary>
public class LocationService : ILocationService
{
    private const int LocationPermissionRequestCode = 1000;
    private static readonly string[] RequiredPermissions =
    [
        Manifest.Permission.AccessFineLocation,
        Manifest.Permission.AccessCoarseLocation
    ];

    /// <inheritdoc/>
    public bool IsLocationAvailable()
    {
        var context = global::Android.App.Application.Context;

        if (context.GetSystemService(Context.LocationService) is not LocationManager locationManager)
        {
            return false;
        }

        // Check if GPS or Network provider is enabled
        var gpsEnabled = locationManager.IsProviderEnabled(LocationManager.GpsProvider);
        var networkEnabled = locationManager.IsProviderEnabled(LocationManager.NetworkProvider);

        return gpsEnabled || networkEnabled;
    }

    /// <inheritdoc/>
    public async Task<bool> RequestLocationPermissionAsync()
    {
        var context = global::Android.App.Application.Context;

        // Check if we already have permission
        if (HasLocationPermission())
        {
            return true;
        }

        // For Android 6.0+ (API 23+), we need to request runtime permissions
        // This requires an Activity context, which we get from MainActivity
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            return false;
        }

        var tcs = new TaskCompletionSource<bool>();
        MainActivity.OnPermissionResult += OnPermissionResultHandler;

        void OnPermissionResultHandler(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == LocationPermissionRequestCode)
            {
                MainActivity.OnPermissionResult -= OnPermissionResultHandler;
                var granted = grantResults.Any(r => r == Permission.Granted);
                tcs.TrySetResult(granted);
            }
        }

        ActivityCompat.RequestPermissions(activity, RequiredPermissions, LocationPermissionRequestCode);

        return await tcs.Task;
    }

    /// <inheritdoc/>
    public async Task<LocationData> GetCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        if (!HasLocationPermission())
        {
            throw new InvalidOperationException("Location permission not granted. Call RequestLocationPermissionAsync first.");
        }

        if (!IsLocationAvailable())
        {
            throw new InvalidOperationException("Location services are not enabled on this device.");
        }

        var context = global::Android.App.Application.Context;

        if (context.GetSystemService(Context.LocationService) is not LocationManager locationManager)
        {
            throw new InvalidOperationException("LocationManager is not available.");
        }

        // Try to get last known location first for quick response
        var lastKnownLocation = GetLastKnownLocation(locationManager);
        if (lastKnownLocation != null)
        {
            return ConvertToLocationData(lastKnownLocation);
        }

        // Request a fresh location update
        var tcs = new TaskCompletionSource<LocationData>();
        var listener = new SingleUpdateLocationListener(location =>
        {
            tcs.TrySetResult(ConvertToLocationData(location));
        });

        // Use cancellation token to allow cancelling the request
        cancellationToken.Register(() =>
        {
            locationManager.RemoveUpdates(listener);
            tcs.TrySetCanceled();
        });

        // Request location from the best available provider
        var provider = GetBestProvider(locationManager) ?? throw new InvalidOperationException("No location provider is available.");

        // SUPPRESSED WARNING CA1416: Platform compatibility
        // RequestLocationUpdates is available on all Android API levels we support (API 23+).
        // The Looper.MainLooper call is also available on all supported API levels.
#pragma warning disable CA1416
        locationManager.RequestLocationUpdates(
            provider,
            minTimeMs: 0,
            minDistanceM: 0,
            listener,
            Looper.MainLooper);
#pragma warning restore CA1416

        // Set a timeout for location request (30 seconds)
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        locationManager.RemoveUpdates(listener);

        if (completedTask == timeoutTask)
        {
            throw new TimeoutException("Location request timed out after 30 seconds.");
        }

        return await tcs.Task;
    }

    private static bool HasLocationPermission()
    {
        var context = global::Android.App.Application.Context;
        var fineLocationGranted = ContextCompat.CheckSelfPermission(context, Manifest.Permission.AccessFineLocation) == Permission.Granted;
        var coarseLocationGranted = ContextCompat.CheckSelfPermission(context, Manifest.Permission.AccessCoarseLocation) == Permission.Granted;
        return fineLocationGranted || coarseLocationGranted;
    }

    private static Location? GetLastKnownLocation(LocationManager locationManager)
    {
        // SUPPRESSED WARNING CA1416: Platform compatibility
        // GetLastKnownLocation is available on all Android API levels we support.
#pragma warning disable CA1416
        var providers = locationManager.GetProviders(enabledOnly: true);
        Location? bestLocation = null;

        foreach (var provider in providers)
        {
            var location = locationManager.GetLastKnownLocation(provider);
            if (location != null && (bestLocation == null || location.Accuracy < bestLocation.Accuracy))
            {
                bestLocation = location;
            }
        }

        return bestLocation;
#pragma warning restore CA1416
    }

    private static string? GetBestProvider(LocationManager locationManager)
    {
        // Modern approach without deprecated Criteria API
        // Prefer GPS for best accuracy, fallback to Network provider

        // Try GPS provider first (most accurate)
        if (locationManager.IsProviderEnabled(LocationManager.GpsProvider))
        {
            return LocationManager.GpsProvider;
        }

        // Fallback to Network provider (less accurate but faster)
        if (locationManager.IsProviderEnabled(LocationManager.NetworkProvider))
        {
            return LocationManager.NetworkProvider;
        }

        // Check for any other enabled providers as last resort
        var providers = locationManager.GetProviders(enabledOnly: true);
        return providers?.FirstOrDefault();
    }

    private static LocationData ConvertToLocationData(Location location)
    {
        return new LocationData
        {
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            Altitude = location.HasAltitude ? location.Altitude : null,
            Accuracy = location.Accuracy,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(location.Time)
        };
    }

    private class SingleUpdateLocationListener(Action<Location> onLocationReceived) : Java.Lang.Object, ILocationListener
    {
        public void OnLocationChanged(Location location)
        {
            onLocationReceived(location);
        }

        public void OnProviderDisabled(string provider)
        {
        }

        public void OnProviderEnabled(string provider)
        {
        }

        public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
        {
        }
    }
}
