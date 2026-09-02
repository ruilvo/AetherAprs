// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Window;
using Avalonia.Android;
using AetherAprs.Services;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace AetherAprs.Android;

[Activity(
    Label = "AetherAprs.Android",
    Theme = "@style/AetherAprsTheme.NoActionBar",
    Icon = "@drawable/icon_400px",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private INavigationService? navigationService;
    private BackInvokedCallback? backInvokedCallback;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Setup the modern back handling for Android 13+
        backInvokedCallback = new BackInvokedCallback(() =>
        {
            HandleBackPressed();
        });
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
        base.OnDestroy();
    }

    private class BackInvokedCallback(Action onBackInvoked) : Java.Lang.Object, IOnBackInvokedCallback
    {
        public void OnBackInvoked()
        {
            onBackInvoked?.Invoke();
        }
    }
}
