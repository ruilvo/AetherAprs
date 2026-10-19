<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Message Templates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users define reusable APRS message templates (e.g. APRS2SOTA spots) with `{name}` placeholders, pick and fill them from the Messages compose screen, and manage them via a dedicated editor page — all stored in appsettings.json, never in the database.

**Architecture:** New `MessageTemplates` section in `AppSettings` bound/saved by the existing `ConfigurationService` (seeded once when the section is absent, sanitized on load). A singleton `MessageTemplateService` wraps the configuration service for CRUD + placeholder substitution. Compose-screen template picking happens in a DialogHost dialog (mirroring `AprsSymbolPickerComponent`); template management is an overlay page reached from Settings. Nothing auto-sends: the filled message lands in the compose text box for review.

**Tech Stack:** .NET 10, Avalonia + Material.Avalonia 3.20.0 + DialogHost.Avalonia, CommunityToolkit.Mvvm, Microsoft.Extensions.Configuration, xUnit v3 + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-19-message-templates-design.md`

## Global Constraints

- Every file gets an SPDX header. C#: `// This file is part of AetherAprs` + `// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>` + `// SPDX-License-Identifier: GPL-3.0-or-later`. AXAML: XML comment variant with the same values.
- `ImplicitUsings` is **disabled** in all projects — write explicit `using` statements for everything (including `System`, `System.Linq`, `System.Collections.Generic`).
- Central package management: no new packages are needed; do not add versions to `.csproj`.
- Namespaces MUST match folder structure exactly.
- Private fields `_camelCase`; `[ObservableProperty]` on `public partial` properties (never backing fields); CommunityToolkit.Mvvm conventions.
- xUnit v3 (`Xunit` namespace; `Assert.ThrowsAsync` works on `ValueTask`), mocking via NSubstitute.
- All user-facing strings via `{loc:Loc Key}` (AXAML) / `Strings.Get("Key")` (C#), added to both `AetherAprs/Localization/Strings.resx` and `Strings.pt.resx` (Portuguese translations required).
- ViewModels never touch EF Core / DbContext; sending always goes through `IMessageService`.
- Error logging via `ILogger<T>` with structured templates — never `Debug.WriteLine`; `async void` only for event handlers, always wrapped in try/catch.
- Consult `CODEBASE_STRUCTURE.md` for file placement; test files mirror source structure under `AetherAprs.Tests/` with namespaces `AetherAprs.Tests.<Subnamespace>`.
- Never use inline DataTemplates for ViewModel→View mapping (ViewLocator handles it); DataTemplates over plain model items (e.g. `MessageTemplate` rows) are fine. Dialog components set `DataContext` directly in code-behind (established `AprsSymbolPickerComponent` precedent).
- Fire-and-forget async only with error handling + UI-state recovery (per AGENTS.md async/await patterns).
- Verification commands: `dotnet build AetherAprs.slnx`, `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj`, `pre-commit run reuse-lint-file --all-files`.
- Max APRS message text = **67 chars** (`public const int MaxTemplateMessageLength = 67` on the template service — `MessageService`'s identical const is private).

## Review Focus

1. **User-typed placeholder values containing `{` or `}`** — substitution must be single-pass; braces in values appear literally in the result and must not be re-substituted. Pinned in Task 2.
2. **All templates deleted, then app restarted** — the seeded default must NOT resurrect (seed only when the raw `MessageTemplates` JSON section is absent). Pinned in Task 1.
3. **Conversation peer has an SSID (e.g. `SOTA-1`, `APRS2SOTA`)** — template filtering matches base callsign, ignoring SSID; no SOTA⇄APRS2SOTA aliasing. Pinned in Task 2.
4. **Filled message is exactly 67 chars** — `Apply` returns it; 68 chars → `null` and the dialog's confirm stays disabled. Pinned in Tasks 2–3.
5. **Malformed entries in appsettings.json (`MessageTemplates` with garbage destination)** — invalid entries are dropped on load, app starts normally, section counts as present (no reseed). Pinned in Task 1.

---

### Task 1: Configuration model, seeding, sanitization, snapshot

**Files:**
- Create: `AetherAprs/Configuration/Settings/MessageTemplate.cs` (namespace `AetherAprs.Configuration.Settings`)
- Create: `AetherAprs/Configuration/Settings/MessageTemplatesSettings.cs` (same namespace)
- Modify: `AetherAprs/Configuration/Settings/AppSettings.cs` — add `public MessageTemplatesSettings MessageTemplates { get; set; } = new();`
- Modify: `AetherAprs/Services/Configuration/ConfigurationService.cs` — seeding + sanitization after binding; extend `GetSettingsSnapshot()`
- Test: `AetherAprs.Tests/Services/Configuration/ConfigurationServiceTests.cs` (extend existing file; namespace `AetherAprs.Tests.Services.Configuration`)

All new C# files in this task get the GPL-3.0-or-later SPDX header (see Global Constraints); folders follow `CODEBASE_STRUCTURE.md` (`Configuration/Settings/` for POCOs, `Services/Configuration/` for the service).

**Interfaces:**
- Consumes: existing `ConfigurationService` ctor + binding flow, `IAppDataDirProviderService`.
- Produces: `MessageTemplate` POCO with `public string Name { get; set; } = "";`, `public string Destination { get; set; } = "";`, `public string Body { get; set; } = "";`; `MessageTemplatesSettings` with `public List<MessageTemplate> Templates { get; set; } = new();`; `AppSettings.MessageTemplates`. These types are consumed by Tasks 2–5.

- [ ] **Step 1: Write the failing tests** in `ConfigurationServiceTests.cs`, following the existing fixture pattern (`CreateTemporaryDirectory()`, `TestAppDataDirProvider`, `NullLogger<ConfigurationService>.Instance`). Tests:
  - `Constructor_SeedsDefaultTemplate_WhenSectionAbsent` — writes an `appsettings.json` with only a `Logging` section; assert `service.Settings.MessageTemplates.Templates` contains exactly one template named `"APRS2SOTA Spot"` with `Destination == "SOTA"` and `Body == "{summit} {freq} {mode} {comment}"`; assert the saved file now contains the `MessageTemplates` section.
  - `Constructor_DoesNotReseed_WhenSectionPresentButEmpty` — writes `appsettings.json` containing `"MessageTemplates": { "Templates": [] }`; assert `Templates` is empty after construction.
  - `Constructor_SanitizesInvalidTemplates` — writes section with three entries: valid one, one with destination `"not valid!!"` (fails `Callsign.TryParse`), one whose name duplicates the valid one (case-insensitive); assert only the valid one remains.
  - `GetSettingsSnapshot_ClonesMessageTemplates` — mutate the snapshot's template list/entries; assert `service.Settings.MessageTemplates.Templates` is unchanged.
- [ ] **Step 2: Run tests to verify they fail** — `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --filter ConfigurationServiceTests` → FAIL (types don't exist).
- [ ] **Step 3: Implement.** Add the two POCOs and the `AppSettings.MessageTemplates` property. In `ConfigurationService` ctor, after the existing bind try/catch: (a) if `_configuration.GetSection("MessageTemplates").Exists()` is false, append the seeded default template and persist via the existing save mechanism; (b) always sanitize the bound list in place: drop entries whose `Destination` fails `Callsign.TryParse` or whose `Name` is null/whitespace, drop later duplicates by case-insensitive name, preserving first occurrence order (needs `using AetherAprs.Models.Aprs;`). Extend `GetSettingsSnapshot()` to deep-clone the templates list (new `MessageTemplate` per entry — plain setters, no validated setters to mirror).
- [ ] **Step 4: Run tests to verify they pass** — same filter command → PASS.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(config): add MessageTemplates settings with seeding and sanitization"`

### Task 2: MessageTemplateService (CRUD, placeholders, peer filtering)

**Files:**
- Create: `AetherAprs/Services/Messaging/IMessageTemplateService.cs` (namespace `AetherAprs.Services.Messaging`)
- Create: `AetherAprs/Services/Messaging/MessageTemplateService.cs` (same namespace)
- Test: `AetherAprs.Tests/Services/Messaging/MessageTemplateServiceTests.cs` (new file; folder exists)

**Interfaces:**
- Consumes: `IConfigurationService` (`Settings`, `UpdateSettingsAsync(Action<AppSettings>)`), `MessageTemplate`, `Callsign.TryParse`.
- Produces:
  ```csharp
  public interface IMessageTemplateService
  {
      public const int MaxTemplateMessageLength = 67; // C# allows const on interface
      IReadOnlyList<MessageTemplate> Templates { get; }
      MessageTemplate? GetByName(string name);
      Task AddAsync(MessageTemplate template, CancellationToken cancellationToken = default);
      Task UpdateAsync(string originalName, MessageTemplate template, CancellationToken cancellationToken = default);
      Task DeleteAsync(string name, CancellationToken cancellationToken = default);
      IReadOnlyList<string> GetPlaceholderNames(string body);
      string? Apply(MessageTemplate template, IReadOnlyDictionary<string, string> values);
      IReadOnlyList<MessageTemplate> GetTemplatesForPeer(Callsign peer);
  }
  ```
  (If `const` on the interface is rejected by style, put `MaxTemplateMessageLength` on `MessageTemplateService` and expose the interface without it — consumers use the concrete type's const via the interface file's documentation.)

- [ ] **Step 1: Write the failing tests** (NSubstitute `Substitute.For<IConfigurationService>()`; set `substitute.Settings` to a real `AppSettings`; capture `UpdateSettingsAsync` actions with `Arg.Do<Action<AppSettings>>(a => a(service.Settings))` wired via `substitute.UpdateSettingsAsync(Arg.Any<Action<AppSettings>>(), ...)` — match the real signature; if it's an extension-less interface method, adapt). Tests:
  - `AddAsync_AppendsTemplate` / `UpdateAsync_ReplacesByName` / `DeleteAsync_RemovesByName` — mutate a copy of `Settings`, capture the action, assert list contents after invoking.
  - `Apply_ReplacesPlaceholdersCaseInsensitively` — body `"{summit} {freq} {mode}"`, values `{"SUMMIT": "ON/ON-010", "Freq": "144.320", "mode": "SSB"}` → `"ON/ON-010 144.320 SSB"`.
  - `Apply_TrimsValues_AndBlanksBecomeEmpty` — value `"  144.320  "` → `144.320`; missing/whitespace-only value → empty string (no collapse of surrounding spaces).
  - `Apply_DoesNotResubstituteBracesInValues` — value `"a {freq} b"` stays literal; result contains `{freq}` once from the value, body placeholders already consumed.
  - `Apply_ReturnsNull_WhenResultExceeds67Chars` — and returns the string at exactly 67.
  - `GetPlaceholderNames_ReturnsDistinctInOrder` — body `"{b} {a} {b} text {c}"` → `["b", "a", "c"]`; name charset letters/digits/underscore.
  - `GetTemplatesForPeer_MatchesBaseIgnoringSsid` — templates with destinations `SOTA` and `OTHER`; peer `new Callsign` parsed from `"SOTA-1"` → only `SOTA` template returned (construct `Callsign` via `Callsign.TryParse("SOTA-1", out var c)`).
- [ ] **Step 2: Run tests to verify they fail** — `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --filter MessageTemplateServiceTests` → FAIL.
- [ ] **Step 3: Implement.** `MessageTemplateService` is `public sealed class` taking `IConfigurationService`. `Templates` reads `_configurationService.Settings.MessageTemplates.Templates` (copy to array). Mutations call `UpdateSettingsAsync` with a lambda that edits the live list (`AddAsync` rejects null/blank names via `ArgumentException` per codebase style). `Apply`: single pass over `GetPlaceholderNames` results, replace each `{name}` case-insensitively via `string.Replace` with `StringComparison.OrdinalIgnoreCase`; trim values; missing → `""`; return null only when `result.Length > MaxTemplateMessageLength`. Placeholder regex: `new Regex(@"\{([A-Za-z0-9_]+)\})", RegexOptions.Compiled`. `GetTemplatesForPeer`: include template when `Callsign.TryParse(template.Destination, out var dest) && dest.Base == peer.Base`.
- [ ] **Step 4: Run tests to verify they pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(messaging): add MessageTemplateService with CRUD and placeholder substitution"`

### Task 3: Template picker dialog ViewModels + ConversationViewModel integration

**Files:**
- Create: `AetherAprs/ViewModels/Components/MessageTemplateDialogViewModel.cs` (namespace `AetherAprs.ViewModels.Components`)
- Create: `AetherAprs/ViewModels/Components/TemplateFieldViewModel.cs` (same namespace)
- Create: `AetherAprs/Factories/ViewModels/IMessageTemplateDialogViewModelFactory.cs` + `AetherAprs/Factories/ViewModels/MessageTemplateDialogViewModelFactory.cs`
- Create: `AetherAprs/Models/Messaging/MessageTemplateDialogResult.cs` — `public sealed record MessageTemplateDialogResult(string Text, string Destination);`
- Modify: `AetherAprs/ViewModels/Pages/ConversationViewModel.cs` — inject `IMessageTemplateService` + `IMessageTemplateDialogViewModelFactory` (both after existing params, logger default stays last); add `MessageTemplateDialogViewModel CreateTemplateDialog()` and `void ApplyTemplateResult(MessageTemplateDialogResult result)`; add `[ObservableProperty] public partial bool IsTemplatePickerEnabled` — hmm, skip extra state; the command/enable logic lives in Task 4's button binding to `CreateTemplateDialogCommand`.
- Test: extend `AetherAprs.Tests/.../ConversationViewModelTests.cs` (update ctor instantiations for the two new params — pass NSubstitute substitutes) + new `AetherAprs.Tests/ViewModels/Components/MessageTemplateDialogViewModelTests.cs`

**Interfaces:**
- Consumes: `IMessageTemplateService` (Task 2), `MessageTemplate`.
- Produces:
  ```csharp
  public sealed partial class TemplateFieldViewModel : ViewModelBase
  {
      public string Name { get; }                 // placeholder name, e.g. "summit"
      [ObservableProperty] public partial string Value { get; set; } // starts ""
  }

  public sealed partial class MessageTemplateDialogViewModel : ViewModelBase
  {
      // ctor(IMessageTemplateService templateService) or receives templates via Initialize:
      void Initialize(IReadOnlyList<MessageTemplate> templates, string? destination);
      IReadOnlyList<MessageTemplate> Templates { get; }          // for ListBox binding
      [ObservableProperty] public partial MessageTemplate? SelectedTemplate { get; set; } // partial void rebuilds Fields
      public ObservableCollection<TemplateFieldViewModel> Fields { get; }
      [ObservableProperty] public partial string Destination { get; set; } // prefilled from template
      public string Preview { get; }             // Apply() with current values, "" when null
      public bool CanConfirm { get; }            // SelectedTemplate != null && Apply() != null
      [RelayCommand] private void Confirm();     // raises a ConfirmRequested event or sets a Result property
      public MessageTemplateDialogResult? Result { get; }  // set by Confirm
      public event EventHandler? CloseRequested; // view/code-behind closes DialogHost with Result
  }

  public interface IMessageTemplateDialogViewModelFactory
  {
      MessageTemplateDialogViewModel Create(IReadOnlyList<MessageTemplate> templates, string? destination);
  }
  ```
  ConversationViewModel additions: `public MessageTemplateDialogViewModel CreateTemplateDialog()` → `Initialize(service.GetTemplatesForPeer(parsedPeer), IsDestinationEditable ? null : DestinationCallsign)`; `public void ApplyTemplateResult(MessageTemplateDialogResult result)` → `MessageText = result.Text;` and `if (IsDestinationEditable) DestinationCallsign = result.Destination;`

- [ ] **Step 1: Write the failing tests.** Dialog VM: selecting a template populates `Fields` from `GetPlaceholderNames` and prefills `Destination`; editing a field updates `Preview` and `CanConfirm` (67-char guard: 67 → true, 68 → false); `Confirm()` sets `Result` with trimmed values applied and raises `CloseRequested`; no selection → `CanConfirm` false. ConversationViewModel: `CreateTemplateDialog` filters by peer (existing convo with `SOTA-1` peer passes peer; new convo passes null) and `ApplyTemplateResult` sets `MessageText` and only sets `DestinationCallsign` when `IsDestinationEditable` was true.
- [ ] **Step 2: Run tests to verify they fail** — `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --filter "MessageTemplateDialogViewModel|ConversationViewModelTests"` → FAIL.
- [ ] **Step 3: Implement** per signatures above. `OnSelectedTemplateChanged` clears+refills `Fields`, sets `Destination` from template (only when not prefilled by caller — caller passes `destination` null to allow template default). `Preview`/`CanConfirm` recomputed in `OnFieldValueChanged` partials on each `TemplateFieldViewModel` (dialog VM subscribes to field `PropertyChanged`, unsubscribing on rebuild — memory-leak rules). Raise `PropertyChanged` for `Preview`/`CanConfirm`.
- [ ] **Step 4: Run tests to verify they pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(messaging): template picker dialog viewmodels wired into ConversationViewModel"`

### Task 4: Template picker dialog UI + compose-screen integration + registrations + localization

**Files:**
- Create: `AetherAprs/Views/Components/MessageTemplateDialogComponent.axaml` + `.axaml.cs` (namespace `AetherAprs.Views.Components`)
- Modify: `AetherAprs/Views/Pages/ConversationPage.axaml` — add a template Button (Material icon button, `avalonia:MaterialIcon Kind="CommentText"` or `Kind="TextSnippet"`) in the compose bar Grid (Row 3) next to Send
- Modify: `AetherAprs/Views/Pages/ConversationPage.axaml.cs` — click handler opens DialogHost
- Modify: `AetherAprs/Factories/ServiceProviderFactory.cs` — register `IMessageTemplateService` singleton (near `IMessageService`, ~line 126) and `IMessageTemplateDialogViewModelFactory` singleton (with the other factories, ~lines 142-144)
- Modify: `AetherAprs/DesignData.cs` — register the same service (design-time fake with the seeded default template) + factory; expose a design-time `MessageTemplateDialogViewModel` property if the component should preview
- Modify: `AetherAprs/Localization/Strings.resx` + `Strings.pt.resx` — keys: `UseTemplate` (button tooltip), `TemplatePickerTitle`, `TemplatePickerTemplate`, `TemplatePickerDestination`, `TemplatePickerPreview`, `TemplatePickerConfirm`, `Cancel`, `TemplatePickerNoTemplates`

**Interfaces:**
- Consumes: `IMessageTemplateDialogViewModelFactory.Create(templates, destination)`, `ConversationViewModel.CreateTemplateDialog()` / `ApplyTemplateResult(result)`, `MessageTemplateDialogResult`.
- Produces: working picker dialog reachable from both new and existing conversations.

- [ ] **Step 1: Implement the component AXAML** — DialogHost content: `TextBlock Classes="Headline6"` title, `ListBox ItemsSource="{Binding Templates}"` item template showing `Name` (+ `Destination` as Caption), `TextBox` per field (ItemsControl over `Fields`, `wpf:TextFieldAssist.Label="{Binding Name}"`, `Text="{Binding Value}"`, `UseFloatingPlaceholder="True"`), destination `TextBox` bound to `Destination` with `TextFieldAssist.Label="{loc:Loc TemplatePickerDestination}"`, preview `TextBlock Classes="Body2"` bound to `Preview`, Cancel (`Classes="Flat"`) + Confirm buttons (Confirm `Command="{Binding ConfirmCommand}"` + `IsEnabled="{Binding CanConfirm}"`). SPX headers on both files.
- [ ] **Step 2: Implement code-behind.** Component `.axaml.cs`: Cancel button handler + `CloseRequested` → `DialogHost.Close("MainDialogHost", (DataContext as MessageTemplateDialogViewModel)?.Result)`. `ConversationPage.axaml.cs`: `private async void OnTemplateButtonClick(object? sender, RoutedEventArgs e)` — mirror `AprsSymbolPickerComponent` precedent: guard `DataContext is ConversationViewModel vm`, wrap in try/catch (log if a logger is available, otherwise swallow per precedent), `var dialogVm = vm.CreateTemplateDialog(); var result = await DialogHost.Show(new MessageTemplateDialogComponent { DataContext = dialogVm }, "MainDialogHost"); if (result is MessageTemplateDialogResult r) vm.ApplyTemplateResult(r);` Wire button `Click` in AXAML.
- [ ] **Step 3: Register** in `ServiceProviderFactory.cs` and `DesignData.cs` (design-time service fake returns the seeded default template from `Templates`). No ViewLocator entry (component, not routed view).
- [ ] **Step 4: Add localization strings** to both `.resx` files (Portuguese translations for all new keys).
- [ ] **Step 5: Build + verify** — `dotnet build AetherAprs.slnx` → success; run existing tests → PASS.
- [ ] **Step 6: Commit** — `git commit -m "feat(messaging): template picker dialog in compose screen"`

### Task 5: Template management overlay page from Settings

**Files:**
- Create: `AetherAprs/ViewModels/Pages/MessageTemplatesViewModel.cs` (namespace `AetherAprs.ViewModels.Pages`)
- Create: `AetherAprs/ViewModels/Components/MessageTemplateEditViewModel.cs` (namespace `AetherAprs.ViewModels.Components`)
- Create: `AetherAprs/Factories/ViewModels/IMessageTemplateEditViewModelFactory.cs` + `AetherAprs/Factories/ViewModels/MessageTemplateEditViewModelFactory.cs`
- Create: `AetherAprs/Views/Pages/MessageTemplatesPage.axaml` + `.axaml.cs`
- Create: `AetherAprs/Views/Components/MessageTemplateEditDialogComponent.axaml` + `.axaml.cs`
- Modify: `AetherAprs/ViewModels/Pages/SettingsViewModel.cs` — inject `INavigationService` (if not present) + `MessageTemplatesViewModel`; add `[RelayCommand] private void OpenMessageTemplates()` → `_navigationService.NavigateTo(_messageTemplatesViewModel)`
- Modify: `AetherAprs/Views/Pages/SettingsPage.axaml` — add a tappable entry (Card row, `Classes="ClickableCard"`, chevron) under an appropriate section bound to `OpenMessageTemplatesCommand`
- Modify: `AetherAprs/Factories/ServiceProviderFactory.cs` + `AetherAprs/DesignData.cs` + `AetherAprs/ViewLocator.cs` — register `MessageTemplatesViewModel` (singleton), `IMessageTemplateEditViewModelFactory` (singleton); ViewLocator mappings: `MessageTemplatesViewModel → new MessageTemplatesPage()`; DesignData exposes a public `MessageTemplatesViewModel` property
- Modify: `AetherAprs/Localization/Strings.resx` + `Strings.pt.resx` — keys: `MessageTemplatesPageTitle`, `MessageTemplatesSectionEntry` (Settings entry label), `MessageTemplatesSectionEntryDescription`, `AddTemplate`, `EditTemplate`, `DeleteTemplate`, `TemplateName`, `TemplateDestination`, `TemplateBody`, `TemplatePlaceholdersHint`, `Save`, `DeleteTemplateConfirmationTitle`, `DeleteTemplateConfirmationMessage`, `TemplatesEmptyState`
- Test: `AetherAprs.Tests/ViewModels/Pages/MessageTemplatesViewModelTests.cs` + extend/new tests for `MessageTemplateEditViewModel` in `AetherAprs.Tests/ViewModels/Components/`

**Interfaces:**
- Consumes: `IMessageTemplateService` (Task 2), `INavigationService`, `IConfirmationDialogService.ShowConfirmationAsync(title, message)`.
- Produces:
  ```csharp
  public sealed partial class MessageTemplatesViewModel : ViewModelBase
  {
      // ctor(IMessageTemplateService templateService, IConfirmationDialogService confirmationDialogService)
      public ObservableCollection<MessageTemplate> Templates { get; }  // refreshed via public void Refresh()
      [RelayCommand] private void Add();       // raises AddRequested (page code-behind opens DialogHost)
      [RelayCommand] private void Edit(MessageTemplate template); // raises EditRequested(template)
      [RelayCommand] private Task DeleteAsync(MessageTemplate template); // confirmation dialog → service.DeleteAsync → Refresh
      public event EventHandler<MessageTemplate?>? AddRequested;
      public event EventHandler<MessageTemplate>? EditRequested;
  }

  public sealed partial class MessageTemplateEditViewModel : ViewModelBase
  {
      void Initialize(MessageTemplate? original, IReadOnlyList<string> existingNames); // null original = add mode
      [ObservableProperty] public partial string Name { get; set; }         // blank → validation error
      [ObservableProperty] public partial string Destination { get; set; }  // Callsign.TryParse validation error
      [ObservableProperty] public partial string Body { get; set; }         // error when > 67 chars (static length check)
      public bool CanSave { get; }   // no errors; duplicate name (case-insensitive, excluding original) → error on Name
      public MessageTemplate BuildTemplate(); // trimmed values
      [RelayCommand] private void Save();    // sets Result, raises CloseRequested
      public MessageTemplate? Result { get; }
      public event EventHandler? CloseRequested;
  }
  ```

- [ ] **Step 1: Write the failing tests.** List VM: `Refresh` copies service templates; `DeleteAsync` without confirmation calls nothing; with confirmation true → `DeleteAsync` on service called (NSubstitute `Received`) and list refreshed. Edit VM: blank name invalid; duplicate name (different case) invalid; invalid destination invalid; body > 67 chars invalid; valid input → `CanSave` true and `BuildTemplate` trims.
- [ ] **Step 2: Run tests to verify they fail** — `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --filter "MessageTemplatesViewModel|MessageTemplateEditViewModel"` → FAIL.
- [ ] **Step 3: Implement** per signatures. Delete confirmation uses `Strings.Get`/`Strings.Format` keys. `MessageTemplatesViewModel` unsubscribes its own events? (it only *raises* them — page code-behind subscribes/unsubscribes on DataContextChanged per memory-leak rules).
- [ ] **Step 4: Implement the pages.** `MessageTemplatesPage`: title `TextBlock Classes="PageTitle"`, back navigation (mirrors other overlay pages), `ItemsControl`/`ListBox` of Material `Card` rows (Name, Destination + Body preview as Caption, Edit/Delete buttons), FloatingButton with Plus icon → Add, empty-state TextBlock. Page code-behind: `AddRequested`/`EditRequested` → build `MessageTemplateEditViewModel` via factory, `DialogHost.Show(new MessageTemplateEditDialogComponent { DataContext = editVm }, "MainDialogHost")`, on result call `AddAsync`/`UpdateAsync(originalName, template)` then `Refresh()` (update original name captured before edit). `SettingsPage` entry → `MessageTemplatesPage` overlay.
- [ ] **Step 5: Registrations** (ServiceProviderFactory, DesignData, ViewLocator) + localization strings in both resx files.
- [ ] **Step 6: Run tests to verify they pass** → PASS; `dotnet build AetherAprs.slnx` → success.
- [ ] **Step 7: Commit** — `git commit -m "feat(messaging): template management page reachable from settings"`

### Task 6: Final verification

**Files:** none (verification only)

- [ ] **Step 1:** `dotnet build AetherAprs.slnx` → 0 errors.
- [ ] **Step 2:** `dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj` → all pass.
- [ ] **Step 3:** `pre-commit run reuse-lint-file --all-files` → pass (all new files have SPDX headers).
- [ ] **Step 4:** Manual smoke check in the Avalonia designer or running desktop app (if available): Settings → Message Templates shows the seeded APRS2SOTA template; Conversation → template button opens picker; filling fields lands text in compose box; nothing auto-sends.
- [ ] **Step 5:** Final commit if any fixups: `git commit -m "chore: message templates verification fixups"` (only if needed).
