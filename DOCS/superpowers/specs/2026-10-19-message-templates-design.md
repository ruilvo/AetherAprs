<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Message Templates — Design

Date: 2026-10-19
Status: Approved design pending build plan

## Intent and why

Sending standardized APRS messages today means typing exact incantations by hand on
a small keyboard. The driving use case is the **APRS2SOTA gateway**
(sotaspots.co.uk): a spot must be an APRS message addressed to `SOTA` (or
`APRS2SOTA`), with a body in a strict format:

```
<summit ref> <freq with units> <mode> [callsign] [comment]
```

Example: `ON/ON-010 144.320MHz SSB ON/G0LGS/P Calling now`

The gateway also accepts plain commands (`/help`, `/spots`, `/alerts`) sent the same
way. Message templates turn these into guided fill-in flows instead of memorized
text.

## Goals

- Users can define message templates with a destination callsign and a body
  containing `{name}` placeholders.
- Templates can be **used** from the Messages UI: pick a template, fill in one
  field per placeholder, get the final message in the compose box.
- Templates can be **created, edited and deleted** through a dedicated UI page.
- Templates live in **appsettings.json** (a `MessageTemplates` section) — NOT in
  the database. Hand-editable, survives alongside other settings.
- A working APRS2SOTA template ships as a seeded default, editable/deletable.

## Non-goals

- No per-template sending history or "last used values" memory (form always
  starts blank).
- No template import/export or sync between devices.
- No validation of gateway-specific body syntax (summit ref format, mode enum).
  The template is generic; only length (≤ 67 chars) is enforced.
- No placeholders in the destination field — destination is a fixed callsign.

## Constraints

- APRS message text max length is 67 chars (`AprsMessageTextMaxLength`,
  enforced by `MessageService`). Filled templates are subject to this.
- All user-facing strings localized via `{loc:Loc}` / `Strings.Get()` in
  `AetherAprs/Localization/Strings.resx` + `Strings.pt.resx`.
- UI uses Material.Avalonia patterns already in the codebase: `controls:Card`,
  `wpf:TextFieldAssist.Label`, `UseFloatingPlaceholder`, `controls:FloatingButton`,
  `DialogHost.Show(view, "MainDialogHost")`, Material typography classes and
  dynamic brushes (`MaterialCardBackgroundBrush`, etc.).
- Every new ViewModel registered in `ServiceProviderFactory.cs`, `DesignData.cs`
  (with exposed property), and mapped in `ViewLocator.cs`.
- REUSE SPDX headers on all new files; namespaces match folder structure;
  ImplicitUsings disabled (explicit usings everywhere); xUnit v3 + NSubstitute
  for tests; central package management (no new packages expected).

## Template model

New POCO in `AetherAprs/Configuration/Settings/` (namespace
`AetherAprs.Configuration.Settings`), added to `AppSettings`:

```csharp
public class AppSettings
{
    public LoggerFilterOptions Logging { get; set; } = new();
    public AprsSettings Aprs { get; set; } = new();
    public MessageTemplatesSettings MessageTemplates { get; set; } = new(); // NEW
}

public class MessageTemplatesSettings
{
    public List<MessageTemplate> Templates { get; set; } = new();
}

public class MessageTemplate
{
    public string Name { get; set; } = string.Empty;        // unique, display name
    public string Destination { get; set; } = string.Empty; // callsign, e.g. "SOTA" (no SSID needed)
    public string Body { get; set; } = string.Empty;        // e.g. "{summit} {freq} {mode} {comment}"
}
```

Placeholder syntax in `Body`: `{name}` — single braces, case-insensitive names,
extracted by regex on use. The same name may appear multiple times in a body; the
form shows one field per unique name and substitutes all occurrences. Literal
braces are not supported (documented limitation; a lone `{` or `}` in a body is
treated as literal text unless it forms a valid `{name}` token).

Seeding: on load, only if the `MessageTemplates` section is **absent** from the
raw configuration (checked via `configuration.GetSection("MessageTemplates").Exists()`
before binding — an empty list is a deliberate user state and must NOT re-seed).
The default is persisted on next save, so the user can edit or delete it and the
change sticks:

```json
"MessageTemplates": {
  "Templates": [
    {
      "Name": "APRS2SOTA Spot",
      "Destination": "SOTA",
      "Body": "{summit} {freq} {mode} {comment}"
    }
  ]
}
```

Validation (mirrors `AprsSettings` style — setters throw on invalid values, bind
failures fall back to defaults):
- `Name`: required, non-empty, unique within the list (enforced at edit time in
  the UI and on load/sanitize in ConfigurationService).
- `Destination`: must parse as a valid `Callsign` (2–6 base chars + optional SSID).
- `Body`: non-empty; may contain any `{name}` tokens.

Persistence goes through the existing `ConfigurationService` (`UpdateSettingser`
/ `SaveSettingsAsync` atomic temp-file + move path). `GetSettingstSnapshot`'s
manual deep clone must be extended to cover the new section.

## Service layer

New `IMessageTemplateService` in `AetherAprs/Services/Messaging/` (namespace
`AetherAprs.Services.Messaging`), singleton:

```csharp
public interface IMessageTemplateService
{
    IReadOnlyList<MessageTemplate> Templates { get; }
    MessageTemplate? GetByName(string name);
    Task AddAsync(MessageTemplate template, CancellationToken ct = default);
    Task UpdateAsync(string originalName, MessageTemplate template, CancellationToken ct = default);
    Task DeleteAsync(string name, CancellationToken ct = default);
    IReadOnlyList<string> GetPlaceholderNames(string body); // unique, order of first appearance
    // Blank placeholder values are allowed and substitute as empty strings;
    // no whitespace collapsing happens at apply time — the result lands in the
    // editable compose box, so the user can tidy leftovers before sending.
    // Returns null only if the filled result exceeds 67 chars.
    string? Apply(MessageTemplate template, IReadOnlyDictionary<string, string> values);
}
```

Implementation wraps `IConfigurationService` (`UpdateSettingsAsync` mutating
`Settings.MessageTemplates`) — no direct file access. No event needed initially;
ViewModels read `Templates` when opening the picker/editor.

## UI: using a template

Entry points (both compose surfaces):
- **New message** (`ConversationPage` when `IsDestinationEditable`): a template
  button in the compose bar opens the picker.
- **Existing conversation**: same button; only templates whose destination
  matches the conversation peer are offered. Matching compares **base callsigns,
  ignoring SSID** (template `SOTA` is offered in a conversation with peer
  `SOTA-1`; `SOTA` and `APRS2SOTA` are different addresses, no aliasing). A
  SOTA template is irrelevant in a chat with a friend. If none match, the
  button can stay visible but shows an empty/hint state — implementation
  detail left to the builder.

Flow (`DialogHost.Show` with `MainDialogHost` identifier):
1. **Picker dialog**: list of templates as Material cards (name, destination,
   body preview). Cancel action.
2. **Placeholder form**: if the template has placeholders, one `TextBox` per
   unique placeholder name (floating label = placeholder name). If none, this
   step is skipped and the body is inserted directly. Fields start blank.
3. The filled message is written into the existing compose `TextBox`
   (`MessageText`); for new conversations the destination is set from the
   template (and the destination field then behaves like a normal sent
   conversation — it locks after send as today). For existing conversations the
   peer is already fixed and matches the template.
4. Nothing is sent automatically by the template flow — the user presses Send.
   The existing `SendAsync` path, retry/ACK machinery, and 67-char validation
   are untouched.

The 67-char limit: `MaxLength=67` is currently set on the compose TextBox; the
form should show the filled length and disable the confirm action if the result
would exceed 67 chars (the compose box MaxLength would otherwise silently
truncate). Exact affordance left to the builder.

## UI: managing templates

A dedicated overlay page (`MessageTemplatesPage` + `MessageTemplatesViewModel`,
transient, navigated via `INavigationService` overlay pattern like
`ConversationViewModel`, `IOverlayRetiretable` not expected to be needed but
builder may add it if event subscriptions require).

Entry point: a link/button on the Settings page ("Message Templates").

Page contents:
- List of templates as cards: name (headline), destination + body preview
  (secondary). Empty state explains templates can be added with a floating
  action button (`controls:FloatingButton`, Material plus icon).
- Tap card → edit dialog; delete affordance on card or in edit dialog (with
  confirmation via the existing `IConfirmationDialogService`).
- Add/edit dialog (DialogHost): fields for Name, Destination (callsign
  validation), Body (multiline, with hint listing current placeholders found).
  Duplicate-name rejection with inline error. Save/Cancel.

## Sending with a template — data flow

```
ConversationViewModel (compose screen)
  → template button → DialogHost picker → placeholder form
  → fills MessageText (+ DestinationCallsign for new convos)
  → existing SendAsync path: IMessageService.SendAsync(callsign, text)
```

No changes to MessageService, ports, or packet flow.

## Files expected to change / be added (builder's guide)

New:
- `AetherAprs/Configuration/Settings/MessageTemplate.cs`, `MessageTemplatesSettings.cs`
- `AetherAprs/Services/Messaging/IMessageTemplateService.cs`, `MessageTemplateService.cs`
- `AetherAprs/Factories/ViewModels/` + ViewModels:
  `MessageTemplatesViewModel`, `MessageTemplateEditViewModel` (or dialog VM),
  template picker VM (location per existing dialog patterns)
- `AetherAprs/Views/Pages/MessageTemplatesPage.axaml(+.cs)`, picker/edit dialog views
- Tests mirroring structure under `AetherAprs.Tests/` (service logic: placeholder
  extraction, apply, length guard, seeding, CRUD round-trip through config)

Modified:
- `AetherAprs/Configuration/Settings/AppSettings.cs` (new section)
- `AetherAprs/Services/Configuration/ConfigurationService.cs` (seed defaults,
  snapshot clone, list sanitization on load)
- `AetherAprs/ViewModels/Pages/ConversationViewModel.cs` + `ConversationPage.axaml`
  (template button in compose bar)
- `AetherAprs/ViewModels/Pages/SettingsViewModel.cs` + `SettingsPage.axaml`
  (entry link)
- `AetherAprs/Factories/ServiceProviderFactory.cs`, `AetherAprs/DesignData.cs`,
  `AetherAprs/ViewLocator.cs` (registrations)
- `AetherAprs/Localization/Strings.resx`, `Strings.pt.resx` (all new strings)

## Decisions made (by the user)

1. Storage: section in appsettings.json (not separate file, not database).
2. Editing: full UI editor (add/edit/delete), backed by the JSON file.
3. Usage: template picker + per-placeholder form in compose; result lands in the
   text box for review before sending.
4. A seeded, editable APRS2SOTA default template ships built-in.
5. Templates available in new conversations AND existing conversations.
6. Placeholder syntax: `{name}`.
7. Editor location: dedicated overlay page reached from Settings.
8. Placeholder form always starts blank (no last-value memory).
9. In existing conversations, templates are matched by base callsign, ignoring
   SSID; no gateway aliasing (`SOTA` ≠ `APRS2SOTA` as template destinations).

## Decisions made by the designer (builder-check round)

- Seeding triggers only when the `MessageTemplates` section is absent from the
  raw config — an explicitly emptied list is never re-seeded.
- Blank placeholder fields are allowed (substitute empty string, e.g. an
  optional `{comment}`); no automatic whitespace collapsing — the user sees and
  can fix the result in the compose box before sending.
