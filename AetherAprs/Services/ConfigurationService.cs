// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
using AetherAprs.Configuration;
using Microsoft.Extensions.Configuration;
using System;

namespace AetherAprs.Services;

public interface IConfigurationService
{
    /// <summary>
    /// Gets the strongly-typed application settings.
    /// </summary>
    AppSettings Settings { get; }

    /// <summary>
    /// Gets the configuration root for advanced scenarios.
    /// </summary>
    IConfiguration Configuration { get; }
}

public class ConfigurationService : IConfigurationService
{
    public AppSettings Settings { get; }
    public IConfiguration Configuration { get; }

    public ConfigurationService()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{GetEnvironment()}.json", optional: true, reloadOnChange: true);

        Configuration = builder.Build();

        // Bind the configuration to strongly-typed settings
        Settings = new AppSettings();
        Configuration.Bind(Settings);
    }

    private static string GetEnvironment()
    {
#if DEBUG
        return "Development";
#else
        return "Production";
#endif
    }
}
