// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Services;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Window;
using Avalonia.Android;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AetherAprs.Android;

[Activity(
    Label = "AetherAprs.Android",
    Theme = "@style/AetherAprsTheme.NoActionBar",
    Icon = "@drawable/icon_400px",
    MainLauncher = true,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private INavigationService? navigationService;
    private BackInvokedCallback? backInvokedCallback;
    private StopPortsBroadcastReceiver? stopPortsReceiver;

    /// <summary>
    /// Gets the current MainActivity instance for permission requests.
    /// </summary>
    public static MainActivity? Instance { get; private set; }

    /// <summary>
    /// Event raised when permission request results are available.
    /// </summary>
    public static event Action<int, string[], Permission[]>? OnPermissionResult;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Localization.UiCulture.Apply(new Services.AndroidUiCultureProvider().GetUiCulture());

        // Store instance for permission requests
        Instance = this;

        // Setup the modern back handling for Android 13+
        backInvokedCallback = new BackInvokedCallback(HandleBackPressed);

        // Register broadcast receiver for stopping ports
        stopPortsReceiver = new StopPortsBroadcastReceiver();
        var filter = new IntentFilter("com.aetheraprs.STOP_ALL_PORTS");
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            RegisterReceiver(stopPortsReceiver, filter, ReceiverFlags.NotExported);
        }
        else
        {
            RegisterReceiver(stopPortsReceiver, filter);
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Get navigation service and subscribe to exit requests
        if (navigationService == null)
        {
            var app = (App?)Avalonia.Application.Current;
            if (app != null)
            {
                navigationService = app.ServiceProvider.GetService<INavigationService>();
                navigationService?.RequestAppExit += OnRequestAppExit;
            }
        }

        // Register callback for modern back handling on Android 13+
        if (backInvokedCallback != null)
        {
            OnBackInvokedDispatcher?.RegisterOnBackInvokedCallback(0, backInvokedCallback);
        }
    }

    protected override void OnPause()
    {
        // Unregister the modern back callback for Android 13+
        if (backInvokedCallback != null)
        {
            OnBackInvokedDispatcher?.UnregisterOnBackInvokedCallback(backInvokedCallback);
        }

        base.OnPause();
    }

    private void HandleBackPressed()
    {
        if (navigationService != null)
        {
            navigationService.GoBack();
        }
        else
        {
            Finish();
        }
    }

    private void OnRequestAppExit(object? sender, EventArgs e)
    {
        // Close the activity when navigation service requests exit
        Finish();
    }

    public override void OnBackPressed()
    {
        HandleBackPressed();
    }

    protected override void OnDestroy()
    {
        navigationService?.RequestAppExit -= OnRequestAppExit;

        // Unregister broadcast receiver
        if (stopPortsReceiver != null)
        {
            UnregisterReceiver(stopPortsReceiver);
            stopPortsReceiver = null;
        }

        // Clear instance reference
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        // Notify any listeners about permission results
        OnPermissionResult?.Invoke(requestCode, permissions, grantResults);
    }

    private class BackInvokedCallback(Action onBackInvoked) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            onBackInvoked?.Invoke();
        }
    }

    private class StopPortsBroadcastReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == "com.aetheraprs.STOP_ALL_PORTS")
            {
                // Get the app and stop all ports
                var app = (App?)Avalonia.Application.Current;
                if (app != null)
                {
                    var portService = app.ServiceProvider.GetService<IPortService>();
                    var logger = app.ServiceProvider.GetService<Microsoft.Extensions.Logging.ILogger<MainActivity>>();
                    
                    if (portService != null)
                    {
                        // Stop all ports asynchronously
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await portService.StopAllPortsAsync();
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "Error stopping ports from notification action");
                            }
                        });
                    }
                }
            }
        }
    }
}
