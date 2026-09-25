<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Messaging Guide

This guide covers sending and receiving APRS messages in AetherAprs.

## Table of Contents
- [Understanding APRS Messages](#understanding-aprs-messages)
- [Viewing Messages](#viewing-messages)
- [Sending Messages](#sending-messages)
- [Message Acknowledgments](#message-acknowledgments)
- [Message Settings](#message-settings)
- [Troubleshooting](#troubleshooting)

## Understanding APRS Messages

APRS messages are **short text communications** between stations, similar to SMS but over APRS networks.

### Message Characteristics

**Format:**
- Plain text only (no emojis, attachments, or rich text)
- Maximum 67 characters per message
- Visible to anyone monitoring the network

**Delivery:**
- **Acknowledged (ACK)**: Recipient confirms receipt
- **Rejected (REJ)**: Recipient cannot process message
- **Timeout**: No response after retries
- **Pending**: Waiting for acknowledgment

**Privacy:**
- Messages are **not encrypted**
- Anyone can read messages on the network
- Not suitable for sensitive information

### Message vs Other Packet Types

| Type | Purpose | Acknowledgment |
|------|---------|----------------|
| Message | Person-to-person text | Yes (ACK/REJ) |
| Position | Location beacon | No |
| Status | General status text | No |
| Bulletin | Broadcast to all | No |

## Viewing Messages

### Messages Page

Tap the **Messages** tab at the bottom to view all conversations.

![Messages Page](screenshots/messages-page.png)
*Screenshot: Messages page showing conversations*

**Conversation list** shows:
- **Callsign**: Who the conversation is with
- **Last message preview**: Recent message snippet
- **Time**: When last message was received/sent
- **Unread indicator**: Badge shows unread count

**Tap a conversation** to open the message thread.

### Conversation View

![Conversation View](screenshots/conversation-view.png)
*Screenshot: Conversation thread with a station*

**Message bubbles:**
- **Right side (blue)**: Messages you sent
- **Left side (gray)**: Messages you received

**Each message shows:**
- Message text
- Timestamp
- Delivery status (for sent messages)

### Delivery Status Indicators

Outbound messages show delivery status below the message:

- **⏱ Pending**: Waiting for acknowledgment
- **✓ Acknowledged**: Recipient confirmed receipt
- **✗ Rejected**: Recipient rejected the message
- **⌛ Timeout**: No response after maximum retries

![Message Status](screenshots/message-status.png)
*Screenshot: Message with delivery status*

### Receiving Messages

**Incoming messages trigger:**
- Notification (Android notification)
- Unread badge on Messages tab
- Unread indicator in conversation list

**Automatic acknowledgment:**
- By default, AetherAprs automatically sends ACK for received messages
- Sender knows you received the message
- Can be disabled in Settings

## Sending Messages

### Starting a New Conversation

**From Messages page:**

1. Tap **Messages** tab
2. Tap **➕ (New Message)** button
3. Enter **destination callsign** (e.g., `CT1ETE`)
4. Optionally add **SSID** (e.g., `CT1ETE-9`)
5. Type your message
6. Tap **Send**

![New Message](screenshots/new-message.png)
*Screenshot: Composing a new message*

### Replying to a Message

**From conversation view:**

1. Tap the conversation from Messages page
2. Type your reply in the text box at bottom
3. Tap **Send** button

### Message Composition

**Guidelines:**
- Maximum 67 characters (counter shows remaining)
- Plain text only (ASCII)
- Avoid special characters that may not transmit correctly
- Keep messages brief for reliable delivery

**Character counter** shows remaining space:
```
[Type message...]                           67
```

**Example messages:**
- ✅ `Thanks for the contact! 73 de CT7ALW`
- ✅ `Roger that, see you at the meeting`
- ✅ `QSL via bureau or direct?`
- ❌ `Hey! 😊 How are you doing today?` (emoji not supported)

### Addressing

**Callsign format**: `CALLSIGN` or `CALLSIGN-SSID`

**Examples:**
- `CT7ALW` - Primary station (SSID 0)
- `CT7ALW-9` - Mobile station
- `CT7ALW-5` - Other network
- `W1ABC-7` - Handheld

**Important**: The SSID **matters**. `CT7ALW` and `CT7ALW-9` are different stations.

## Message Acknowledgments

APRS messages use a **retry mechanism** to ensure delivery.

### How ACK/REJ Works

**When you send a message:**

1. **Transmission**: Message sent to all TX-enabled ports
2. **Wait for ACK**: System waits for acknowledgment
3. **Retry if needed**: Retransmits if no response
4. **Exponential backoff**: Each retry waits longer
5. **Eventually timeout**: Gives up after max retries

**Retry example:**
```
Send attempt 1 → wait 30 seconds
Send attempt 2 → wait 60 seconds
Send attempt 3 → wait 120 seconds
Send attempt 4 → wait 240 seconds
Timeout → marked as failed
```

**When you receive a message:**

1. **Receive**: Message arrives from network
2. **Display**: Shown in conversation
3. **Auto-ACK**: AetherAprs automatically sends acknowledgment
4. **Sender notified**: Sender sees "Acknowledged" status

### Acknowledgment Responses

**ACK (Acknowledged):**
- Recipient successfully received message
- Displayed as **✓ Acknowledged**
- No further retries

**REJ (Rejected):**
- Recipient could not process message
- Rare (usually protocol issues)
- Displayed as **✗ Rejected**
- No further retries

**Timeout:**
- No response after maximum retries
- Recipient may be offline or out of range
- Displayed as **⌛ Timeout**
- Message stored for later delivery attempt

### Viewing Delivery Status

In conversation view, outbound messages show status below text:

![Delivery Status](screenshots/delivery-status-detail.png)
*Screenshot: Message delivery status details*

**Status updates automatically** as acknowledgments arrive.

## Message Settings

Configure message behavior in Settings.

### Accessing Message Settings

1. Tap **Settings** tab
2. Scroll to **Messaging** section

![Message Settings](screenshots/message-settings.png)
*Screenshot: Messaging configuration*

### Auto-Acknowledge Messages

**Setting**: "Auto-acknowledge incoming messages"

- **☑️ Enabled (default)**: Automatically send ACK for received messages
- **☐ Disabled**: Do not send ACK (messages still received)

**Why disable?**
- Operating read-only (monitor only)
- Don't want to confirm receipt
- Running receive-only port configuration

**Impact**: Senders won't know you received their message if disabled.

### Maximum Retry Attempts

**Setting**: "Maximum retry attempts"

**Default**: 4 attempts

**Range**: 1-10 attempts

**Description**: How many times to retransmit a message before giving up.

**Examples:**
- `1`: Send once, no retries (fast failure)
- `4`: Send up to 4 times (balanced)
- `10`: Aggressive retries (may succeed eventually but takes time)

**Recommendation**: 3-5 retries for most use cases.

### Initial Retry Timeout

**Setting**: "Initial retry timeout (seconds)"

**Default**: 30 seconds

**Range**: 10-300 seconds

**Description**: How long to wait for ACK before first retry.

**Exponential backoff**: Each subsequent retry waits twice as long.

**Examples:**
- `30`: Wait 30s, then 60s, then 120s, then 240s
- `60`: Wait 60s, then 120s, then 240s, then 480s

**Recommendation**: 30-60 seconds for typical APRS networks.

### Configuration Examples

**Fast delivery (local network):**
- Max retries: 3
- Initial timeout: 15 seconds
- **Result**: Quick failures, suitable for local RF with good coverage

**Reliable delivery (long-distance):**
- Max retries: 6
- Initial timeout: 60 seconds
- **Result**: Patient retries, suitable for weak signals or APRS-IS delays

**Monitor-only:**
- Auto-acknowledge: Disabled
- **Result**: Receive messages but don't respond

## Message Storage

### Database Persistence

All messages are stored in the SQLite database:
- **Sent messages**: Stored with delivery status
- **Received messages**: Stored with timestamp
- **Retention**: Subject to packet retention settings (default 30 days)

### Conversation History

Messages remain in conversations until:
- Packet retention period expires (Settings → Display)
- App data cleared (Android Settings → Apps → AetherAprs → Clear Data)

**Note**: Clearing app data deletes **all stored data** including messages, packets, and port configurations.

## Best Practices

### Message Content

**Do:**
- Keep messages brief and clear
- Use standard abbreviations (73, QSL, etc.)
- Include your callsign at end if helpful
- Use APRS message conventions

**Don't:**
- Send sensitive information (not encrypted)
- Spam or send excessive messages
- Send overly long messages (use multiple if needed)
- Use special characters that may corrupt

### Network Etiquette

**Be considerate:**
- APRS is a shared resource
- Avoid unnecessary messages
- Don't flood the network
- Respond to messages when appropriate

**Frequency usage:**
- Messages on RF use channel bandwidth
- Excessive retries can cause congestion
- Use reasonable retry settings

### When to Use Messages

**Good uses:**
- Quick status updates
- Contact arrangements
- Information queries
- Event coordination

**Poor uses:**
- Long conversations (use phone/email instead)
- Sensitive information (not secure)
- Real-time chat (high latency)
- File transfers (not supported)

## Troubleshooting

### Messages Not Sending

**Check ports:**
- At least one TX-enabled port?
- Port status shows **"Running"**?
- APRS-IS: Valid passcode?

**Check network:**
- APRS-IS connected to server?
- RF: TNC connected to radio?
- Radio keying up (PTT working)?

**Check recipient:**
- Callsign spelled correctly?
- Correct SSID?
- Recipient online and monitoring?

### Messages Stuck in "Pending"

**Possible causes:**
- Recipient offline or out of range
- Poor RF coverage
- APRS-IS filter blocking acknowledgment
- Recipient not running APRS software

**Solutions:**
- Wait for retry timeout
- Verify recipient is active on aprs.fi
- Try sending via different port (RF vs APRS-IS)
- Contact recipient via alternate means

### Not Receiving Messages

**Check settings:**
- Auto-acknowledge enabled? (should be)
- At least one RX-enabled port?

**Check ports:**
- Port connected?
- APRS-IS: Filter allows messages? (use `t/m` or no filter)

**Check callsign:**
- Callsign correct in Settings?
- SSID matches what sender is using?

### Messages Received But Can't Reply

**Check send requirements:**
- Need TX-enabled port
- APRS-IS: Need valid passcode (not `-1`)
- RF: TNC and radio must be working

**Workaround:**
- Add APRS-IS port with valid passcode for replies
- Enable TX on existing port

### Acknowledgments Not Working

**Sender perspective:**
- Wait for retry timeout
- Check recipient is online
- Verify message was delivered (check aprs.fi raw packets)

**Recipient perspective:**
- Ensure auto-acknowledge enabled
- Check TX-enabled port available
- Verify acknowledgment transmitted (check Packets page)

### Foreign Characters Not Displaying

**APRS limitation:**
- APRS supports ASCII only
- Extended characters may not transmit correctly
- Some clients support UTF-8, others don't

**Solution:**
- Use plain ASCII (A-Z, 0-9, basic punctuation)
- Avoid accented characters (á, é, ñ, etc.)
- Avoid emoji and symbols

## Advanced Topics

### Message IDs

APRS messages include a **message ID** for tracking acknowledgments:

```
:CT7ALW   :Hello there{12345
```

- `{12345`: Message ID
- ACK response: `ack12345`
- REJ response: `rej12345`

**AetherAprs handles this automatically**. You don't need to manage message IDs manually.

### Message Routing

Messages can be sent via:
- **APRS-IS**: Delivered via Internet to recipient
- **RF**: Transmitted on radio, digipeated as needed
- **Both**: Sent via all TX-enabled ports simultaneously

**Best reliability**: Enable both APRS-IS and RF ports for dual-path delivery.

### Message Format

Standard APRS message format:

```
:CALLSIGN:Message text{msgID
```

**Components:**
- `:CALLSIGN`: Addressee (9 characters, padded with spaces)
- `:Message text`: Your message
- `{msgID`: Unique message identifier

**Example:**
```
:CT7ALW   :Thanks for the contact! 73{AB12D
```

AetherAprs formats this automatically.

---

**Next**: [Digipeater Configuration →](Digipeater-Configuration.md)
