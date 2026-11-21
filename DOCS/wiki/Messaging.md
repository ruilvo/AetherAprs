<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Messaging

APRS messages are public network traffic. Do not use them for private or sensitive
information. AetherAprs stores conversation history and tracks delivery state for
outbound messages.

## Start or continue a conversation

1. Open **Messages**.
2. Tap **New Message** and enter a destination callsign, including an SSID if needed.
   To continue an existing conversation, select it from the list.
3. Enter a message (up to 67 characters) and tap **Send**.

Sending requires an enabled TX port and a valid station callsign. The message list
shows the peer callsign and a preview of the most recent message.

The conversation view identifies your messages and received messages in text. Outbound
messages show their delivery status. Incoming messages can produce an Android
notification; tapping it opens the conversation.

## Acknowledgments and retries

When the sender requests an acknowledgment, AetherAprs processes ACK/REJ responses and
retries an unacknowledged outgoing message according to the configured retry limit and
timeout. A missing acknowledgment can mean the recipient is offline, out of range, or
unable to respond.

In **Settings → Messaging**, you can enable automatic acknowledgment for incoming
messages and change the retry count and initial timeout.

## Templates

Open **Settings → Messaging → Message Templates** to manage saved messages. From a
conversation, use the template button beside the message entry box to fill in a
template before sending.

## Troubleshooting

- Check that a TX port is enabled and its APRS-IS passcode or TNC connection is valid.
- Include the recipient's SSID if their station uses one.
- Review APRS-IS filtering if messages or acknowledgments are missing.
- A delivery timeout does not prove the recipient never saw the message.
