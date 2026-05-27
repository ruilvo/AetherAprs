// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Aprs;

using AetherAprs.Protocols;
using System;
using System.Threading;
using System.Threading.Tasks;

public interface IAprsSessionService
{
    Task<Guid> RegisterBackendAsync(IAprsBackend backend, CancellationToken cancellationToken = default);

    Task UnregisterBackendAsync(Guid handle, CancellationToken cancellationToken = default);
}
