// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

namespace AetherAprs.Messages;

using AetherAprs.Models;
using CommunityToolkit.Mvvm.Messaging.Messages;

public sealed class SendAprsPacketMessage(AprsPacket value) : ValueChangedMessage<AprsPacket>(value)
{
}
