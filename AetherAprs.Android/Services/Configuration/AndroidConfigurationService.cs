// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using AetherAprs.Services.Configuration;

namespace AetherAprs.Android.Services.Configuration;

/// <summary>
/// Android settings service. Stores settings in the app's local data folder.
/// </summary>
public class AndroidConfigurationService : ConfigurationService
{
    protected override string GetSettingsBasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDir = Path.Combine(appData, "AetherAprs");

        if (!Directory.Exists(settingsDir))
        {
            Directory.CreateDirectory(settingsDir);
        }

        var settingsPath = Path.Combine(settingsDir, DefaultFileName);
        if (!File.Exists(settingsPath))
        {
            File.WriteAllText(settingsPath, "{}");
        }

        return settingsDir;
    }
}
