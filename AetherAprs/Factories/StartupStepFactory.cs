// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AetherAprs.Data;
using AetherAprs.Services.Configuration;
using AetherAprs.Services.Maintenance;
using AetherAprs.Services.Packets;
using AetherAprs.Services.Platform;
using AetherAprs.Services.Ports;
using AetherAprs.Services.Startup;
using AetherAprs.ViewModels.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AetherAprs.Factories;

/// <summary>
/// Composes the ordered application startup sequence from the configured services.
/// </summary>
/// <remarks>
/// This is the only place that knows the order. Each step resolves its own
/// dependencies, so a step can be reordered, made optional or removed without
/// changing the services it drives.
/// </remarks>
internal static class StartupStepFactory
{
    private const string PortPermissionLoggerCategory = "AetherAprs.Services.Startup.PortPermissions";

    /// <summary>
    /// Builds the startup steps for a configured service provider.
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <returns>The steps in the order they must run.</returns>
    internal static IReadOnlyList<StartupStep> Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return
        [
            // The schema must exist before anything reads configuration or history.
            new StartupStep("database", _ =>
            {
                services.GetRequiredService<AppSavedDataInitializer>().Initialize();
                return Task.CompletedTask;
            }),

            // Resolving the adapter loads and validates the settings file; reading
            // Current surfaces a load failure as this step's failure.
            new StartupStep("settings", cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Resolving the adapter loads and validates the settings file;
                // reading Current surfaces a load failure as this step's failure.
                _ = services.GetRequiredService<IConfigurationService>().Current;
                return Task.CompletedTask;
            }),

            // Port configuration comes from the database the first step prepared.
            new StartupStep("ports", cancellationToken =>
                services.GetRequiredService<IPortService>().InitializeAsync(cancellationToken)),

            // Permission prompts are optional: a denied permission degrades the
            // feature that needs it, it does not stop the application.
            new StartupStep("permissions", cancellationToken =>
                RequestPortPermissionsAsync(services, cancellationToken), isRequired: false),

            new StartupStep("location", cancellationToken =>
                StartLocationTrackingAsync(services, cancellationToken), isRequired: false),

            // Deferred retention work, so it never competes with the steps above.
            new StartupStep("maintenance", cancellationToken =>
                services.GetRequiredService<IDatabaseMaintenanceWorker>().StartAsync(cancellationToken),
                isRequired: false),

            // Ingest and forwarding consumers must be draining before any port can
            // deliver a packet or a stored packet needs forwarding.
            new StartupStep("consumers", cancellationToken =>
                StartConsumersAsync(services, cancellationToken)),

            // Last: arming starts connecting ports.
            new StartupStep("sessions", cancellationToken =>
                services.GetRequiredService<SessionStartup>().StartAsync(cancellationToken))
        ];
    }

    private static async Task StartConsumersAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.GetRequiredService<IPacketIngestPipeline>()
            .StartAsync(cancellationToken)
            .ConfigureAwait(false);
        await services.GetRequiredService<IDigipeatRouter>()
            .StartAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static Task RequestPortPermissionsAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        PortStartupPermissions.RequestAsync(
            services.GetRequiredService<IPortService>().Ports,
            services.GetRequiredService<IPermissionService>(),
            services.GetRequiredService<ILoggerFactory>().CreateLogger(PortPermissionLoggerCategory),
            cancellationToken);

    private static async Task StartLocationTrackingAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await services.GetRequiredService<HomeViewModel>().StartLocationTrackingAsync().ConfigureAwait(false);
    }
}
