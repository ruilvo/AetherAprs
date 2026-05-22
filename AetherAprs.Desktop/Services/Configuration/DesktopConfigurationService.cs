// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using AetherAprs.Services.Configuration;

namespace AetherAprs.Desktop.Services.Configuration;

/// <summary>
/// Desktop settings service. Stores settings alongside the application binary.
/// </summary>
public class DesktopConfigurationService : ConfigurationService
{
    protected override string GetSettingsBasePath() => AppContext.BaseDirectory;
}
