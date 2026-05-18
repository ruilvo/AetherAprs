// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace AetherAprs.Android;

[Activity(
    Label = "AetherAprs.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon_400px",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
