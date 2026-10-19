// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Models;
using AetherAprs.Services.Platform;
using Android;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Common;
using Android.Gms.Location;
using Android.Locations;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AetherAprs.Android.Services.Platform;

/// <summary>
/// Android-specific implementation of ILocationService.
/// Prefers Google Play Services' fused location provider (network + GPS fusion,
/// fast first fix even indoors) and falls back to a concurrent multi-provider
/// race via Android's LocationManager on devices without Google Play Services.
/// </summary>
public class LocationService : ILocationService
{
    private const int LocationPermissionRequestCode = 1001;
    private static readonly string[] RequiredPermissions =
    [
        Manifest.Permission.AccessFineLocation,
        Manifest.Permission.AccessCoarseLocation
    ];

    // Interval for fused provider update requests (first fix is what matters;
    // updates are removed as soon as one fix arrives).
    private const int FusedIntervalMs = 1000;

    // Last-known-location cache is accepted when younger than this. The tracking
    // loop refines the position on subsequent polls, so a slightly stale fix is
    // far better than waiting for a fresh one on a cold GPS start.
    private const double LastKnownMaxAgeSeconds = 120;

    // The fused provider usually reports a fix within a few seconds; if it cannot,
    // fall back to LocationManager instead of making the user wait.
    private static readonly TimeSpan FusedRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LocationManagerRequestTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<LocationService>? _logger;

    public LocationService(ILogger<LocationService>? logger = null)
    {
        _logger = logger;
    }

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

        // Permissions are requested by MainActivity.OnResume()
        // If we get here and don't have permission, request it again
        var activity = MainActivity.Instance;
        if (activity == null)
        {
            return false;
        }

        // TaskCreationOptions.RunContinuationsAsynchronously prevents the permission
        // callback thread from being hijacked by the awaiting continuation.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPermissionResultHandler(int requestCode, string[] permissions, Permission[] grantResults)
        {
            if (requestCode == LocationPermissionRequestCode)
            {
                MainActivity.OnPermissionResult -= OnPermissionResultHandler;
                var granted = grantResults.Any(r => r == Permission.Granted);
                tcs.TrySetResult(granted);
            }
        }

        // If no callback ever arrives (e.g. activity recreated while the dialog was
        // pending), the handler must not leak in the static event invocation list and
        // the caller must not hang forever: cancel after a bounded wait.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var registration = timeoutCts.Token.Register(() =>
        {
            MainActivity.OnPermissionResult -= OnPermissionResultHandler;
            tcs.TrySetCanceled(timeoutCts.Token);
        });

        MainActivity.OnPermissionResult += OnPermissionResultHandler;

        ActivityCompat.RequestPermissions(activity, RequiredPermissions, LocationPermissionRequestCode);

        return await tcs.Task.ConfigureAwait(false);
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

        // Try the last known location first for a quick response
        var lastKnownLocation = GetLastKnownLocation(locationManager);
        if (lastKnownLocation != null && IsFresh(lastKnownLocation))
        {
            return ConvertToLocationData(lastKnownLocation);
        }

        // Prefer the fused provider (Google Play Services): it fuses network and GPS
        // positioning and typically reports a fix within seconds, even indoors.
        if (IsGooglePlayServicesAvailable())
        {
            var fusedLocation = await TryGetCurrentLocationViaFusedAsync(cancellationToken);
            if (fusedLocation != null)
            {
                return fusedLocation;
            }
        }

        // Fallback: race all enabled LocationManager providers concurrently and take
        // the first fix. Network positioning is near-instant while GPS alone can take
        // minutes on a cold start (or never fix at all indoors).
        return await GetCurrentLocationViaLocationManagerAsync(locationManager, cancellationToken);
    }

    private static bool HasLocationPermission()
    {
        var context = global::Android.App.Application.Context;
        var fineLocationGranted = ContextCompat.CheckSelfPermission(context, Manifest.Permission.AccessFineLocation) == Permission.Granted;
        var coarseLocationGranted = ContextCompat.CheckSelfPermission(context, Manifest.Permission.AccessCoarseLocation) == Permission.Granted;
        return fineLocationGranted || coarseLocationGranted;
    }

    private static bool IsGooglePlayServicesAvailable()
    {
        try
        {
            var availability = GoogleApiAvailability.Instance;
            if (availability == null)
            {
                return false;
            }

            return availability.IsGooglePlayServicesAvailable(global::Android.App.Application.Context) == ConnectionResult.Success;
        }
        catch
        {
            // De-Googled ROMs can fail while resolving GMS classes; treat as unavailable.
            return false;
        }
    }

    private static bool IsFresh(Location location)
    {
        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(location.Time);
        return age.TotalSeconds < LastKnownMaxAgeSeconds;
    }

    private async Task<LocationData?> TryGetCurrentLocationViaFusedAsync(CancellationToken cancellationToken)
    {
        try
        {
            var fusedClient = LocationServices.GetFusedLocationProviderClient(global::Android.App.Application.Context);

            var tcs = new TaskCompletionSource<Location>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callback = new FirstFixLocationCallback(location => tcs.TrySetResult(location));

            var locationRequest = new global::Android.Gms.Location.LocationRequest.Builder(Priority.PriorityHighAccuracy, FusedIntervalMs)
                .SetWaitForAccurateLocation(false)
                .Build();

            await using var cancellationRegistration = cancellationToken.Register(() =>
                tcs.TrySetCanceled(cancellationToken));

            fusedClient.RequestLocationUpdates(locationRequest, callback, Looper.MainLooper);

            try
            {
                var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(FusedRequestTimeout, CancellationToken.None));

                if (completedTask != tcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger?.LogWarning("Fused location provider did not deliver a fix within {TimeoutSeconds}s; falling back to LocationManager.", FusedRequestTimeout.TotalSeconds);
                    return null;
                }

                return ConvertToLocationData(await tcs.Task);
            }
            finally
            {
                fusedClient.RemoveLocationUpdates(callback);
            }
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any failure on the fused path (missing or broken GMS components) must
            // not break location tracking: fall back to LocationManager.
            _logger?.LogWarning(ex, "Fused location provider failed; falling back to LocationManager.");
            return null;
        }
    }

    private static async Task<LocationData> GetCurrentLocationViaLocationManagerAsync(LocationManager locationManager, CancellationToken cancellationToken)
    {
        // SUPPRESSED WARNING CA1416: Platform compatibility
        // GetProviders is available on all Android API levels we support.
        var providers = locationManager.GetProviders(enabledOnly: true)
            .Where(p => p != LocationManager.PassiveProvider)
            .ToList();

        if (providers.Count == 0)
        {
            throw new InvalidOperationException("No location provider is available.");
        }

        var tcs = new TaskCompletionSource<LocationData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new SingleUpdateLocationListener(location =>
        {
            tcs.TrySetResult(ConvertToLocationData(location));
        });

        // Use cancellation token to allow cancelling the request.
        // Registration disposed via using so the lambda is not rooted for the token's lifetime.
        await using var cancellationRegistration = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled(cancellationToken);
        });

        // SUPPRESSED WARNING CA1416: Platform compatibility
        // RequestLocationUpdates is available on all Android API levels we support (API 23+).
        // The Looper.MainLooper call is also available on all supported API levels.
        foreach (var provider in providers)
        {
            locationManager.RequestLocationUpdates(
                provider,
                minTimeMs: 0,
                minDistanceM: 0,
                listener,
                Looper.MainLooper);
        }

        try
        {
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(LocationManagerRequestTimeout, CancellationToken.None));

            if (completedTask != tcs.Task)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException($"Location request timed out after {LocationManagerRequestTimeout.TotalSeconds} seconds across {providers.Count} provider(s).");
            }

            return await tcs.Task;
        }
        finally
        {
            // RemoveUpdates unregisters the listener from every provider at once.
            locationManager.RemoveUpdates(listener);
        }
    }

    private static Location? GetLastKnownLocation(LocationManager locationManager)
    {
        // SUPPRESSED WARNING CA1416: Platform compatibility
        // GetLastKnownLocation is available on all Android API levels we support.
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
    }

    private static LocationData ConvertToLocationData(Location location)
    {
        return new LocationData
        {
            Location = new Geo.Coordinate(location.Latitude, location.Longitude),
            Altitude = location.HasAltitude ? location.Altitude : null,
            Accuracy = location.Accuracy,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(location.Time)
        };
    }

    private sealed class SingleUpdateLocationListener(Action<Location> onLocationReceived) : Java.Lang.Object, global::Android.Locations.ILocationListener
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

    private sealed class FirstFixLocationCallback(Action<Location> onFirstFix) : LocationCallback
    {
        public override void OnLocationResult(LocationResult result)
        {
            var location = result?.LastLocation;
            if (location != null)
            {
                onFirstFix(location);
            }
        }
    }
}
