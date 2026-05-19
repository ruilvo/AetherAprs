using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace AetherAprs.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly IConfiguration _configuration;
    public AppSettings Settings { get; private set; }

    public ConfigurationService()
    {
        var environment = Environment.GetEnvironmentVariable("NETCORE_ENVIRONMENT") ?? "Production";

        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false);

        _configuration = builder.Build();
        Settings = _configuration.GetSection("AppSettings").Get<AppSettings>() ?? new AppSettings();
    }
}
