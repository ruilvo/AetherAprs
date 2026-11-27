// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.Ports;

namespace AetherAprs.Data.Entities;

/// <summary>
/// Singleton row holding the last persisted session lifecycle state.
/// </summary>
/// <remarks>
/// Only runtime transitions are stored; the operator's arm intent is never
/// persisted, so a restarted process always comes up disarmed.
/// </remarks>
public sealed class SessionStateRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public SessionLifecycleState State { get; set; } = SessionLifecycleState.Stopped;
}
