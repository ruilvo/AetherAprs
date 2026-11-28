// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace AetherAprs.Factories;

/// <summary>
/// The platform's explicit replacement protocol for core registrations.
/// </summary>
/// <remarks>
/// A platform used to override a core service by registering its own descriptor
/// last and relying on "last registration wins". That made every override an
/// undocumented ordering assumption and silently left the core implementation
/// resolvable through another service type. Replacing is now a named operation
/// that fails when the core does not register the service being replaced.
/// </remarks>
/// <param name="services">The container being built.</param>
public sealed class CoreServiceOverrides(IServiceCollection services)
{
    private readonly IServiceCollection _services =
        services ?? throw new ArgumentNullException(nameof(services));

    /// <summary>
    /// Replaces the core registration of <typeparamref name="TService"/> with a
    /// platform implementation.
    /// </summary>
    /// <typeparam name="TService">The core service to replace.</typeparam>
    /// <typeparam name="TImplementation">The platform implementation.</typeparam>
    /// <exception cref="InvalidOperationException">
    /// The core container does not register <typeparamref name="TService"/>, so
    /// this is either a typo or a service the platform invented.
    /// </exception>
    public void Replace<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        int index = FindLastRegistration(typeof(TService));
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The core container does not register {typeof(TService).FullName}, " +
                "so the platform cannot replace it.");
        }

        _services[index] = ServiceDescriptor.Singleton<TService, TImplementation>();
    }

    private int FindLastRegistration(Type serviceType)
    {
        for (int index = _services.Count - 1; index >= 0; index--)
        {
            if (_services[index].ServiceType == serviceType)
            {
                return index;
            }
        }

        return -1;
    }
}
