// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Services.Platform;

/// <summary>
/// Shows the platform's permission prompt for a queued request.
/// </summary>
/// <remarks>
/// The host only starts the prompt. Android delivers the answer on a separate
/// callback rather than a task, so the result is reported back to
/// <see cref="PermissionRequestQueue.Complete"/> with the request id the host
/// received. A host that cannot show prompts reports that by throwing.
/// </remarks>
public interface IPermissionPromptHost
{
    /// <summary>
    /// Shows the prompt for <paramref name="kind"/> and reports the answer later
    /// under <paramref name="requestId"/>.
    /// </summary>
    /// <param name="kind">The permission to ask for.</param>
    /// <param name="requestId">The id the result callback must use.</param>
    void Show(PermissionKind kind, int requestId);
}
