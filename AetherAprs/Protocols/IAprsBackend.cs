// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Protocols;

using AetherAprs.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface IAprsBackend
{
    IAsyncEnumerable<AprsPacket> ReceiveAsync(CancellationToken cancellationToken = default);

    ValueTask SendAsync(AprsPacket packet, CancellationToken cancellationToken = default);
}
