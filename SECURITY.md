<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Security policy

## Supported versions

AetherAprs has not published a GA release. Security fixes are applied to `main`; release
tags are supported from the moment they are published until a newer release supersedes
them.

## Reporting a vulnerability

Report vulnerabilities privately through GitHub's **Security** tab → **Advisories** →
**Report a vulnerability** on <https://github.com/ruilvo/AetherAprs>. Do not open a public
issue, discussion, or pull request for an unfixed vulnerability.

Please include:

- the affected component and version or commit;
- reproduction steps or a proof of concept that does not contain real credentials;
- the impact you believe it has (for example credential disclosure, remote packet
  injection, or denial of service);
- any suggested mitigation.

You will receive an acknowledgement as soon as the report is triaged. Please allow time
for a fix and a coordinated disclosure date before publishing details.

## Scope

Security-relevant areas of this project include:

- APRS-IS passcodes and operator credentials stored through the platform secret store;
- the diagnostics and support report, which must never contain passcodes, message text,
  or packet payloads;
- Bluetooth, TCP, and audio transports, including malformed frame handling;
- Android exported components, deep links, notifications, and foreground-service state;
- database and configuration files, including backup exclusion and corruption handling.

The following are not vulnerabilities by themselves: missing TLS on a legacy APRS-IS
server that the operator explicitly configured, the absence of an account system (the app
is local-first by design), and radio-frequency behaviour that follows the documented APRS
conventions.

## Handling of secrets

Never include real callsigns, passcodes, API keys, keystores, or location histories in a
report, log excerpt, screenshot, or test fixture. Use synthetic values. If a credential
may have been exposed, rotate it and note the exposure in the report without repeating
the value.
