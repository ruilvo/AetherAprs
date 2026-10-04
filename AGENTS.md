<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# AGENTS.md

Quick-reference instructions for AI agents working in the AetherAprs codebase.

## Project Overview

AetherAprs is a cross-platform ham radio APRS application built with Avalonia UI and .NET 10. Three projects:

| Project | Path | Target |
|---|---|---|
| Core library | `AetherAprs/` | net10.0 |
| Android app | `AetherAprs.Android/` | net10.0-android |
| Test project | `AetherAprs.Tests/` | net10.0 |

## Build Commands

```powershell
# Build solution
dotnet build AetherAprs.slnx

# Build specific project
dotnet build AetherAprs/AetherAprs.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj

# Run desktop (if supported on platform)
dotnet run --project AetherAprs/AetherAprs.csproj

# Build only the test project
dotnet build AetherAprs.Tests/AetherAprs.Tests.csproj

# Run all tests
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj

# Run tests without rebuilding (after a successful build)
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --no-build
```

## Critical Requirements

### REUSE License Compliance

Every file MUST have an SPDX license header. Pre-commit hook enforces this via `reuse-lint-file`.

Code files (C#):
```csharp
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
```

XML files (.csproj, .axaml, .slnx):
```xml
<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: GPL-3.0-or-later
-->
```

YAML/config files:
```yaml
# This file is part of AetherAprs
# SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
# SPDX-License-Identifier: CC0-1.0
```

Verify compliance: `pre-commit run reuse-lint-file --all-files`

### ImplicitUsings Disabled

All projects have `<ImplicitUsings>disable</ImplicitUsings>`. You MUST include explicit using statements for everything, including `System`, `System.Collections.Generic`, etc. Do not assume any default usings.

### Central Package Management

Package versions are centralized in `Directory.Packages.props`. When adding a new package:

1. Add version to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="PackageName" Version="x.y.z" />
   ```

2. Reference without version in `.csproj`:
   ```xml
   <PackageReference Include="PackageName" />
   ```

Do NOT specify versions in individual project files.

## Codebase Structure

**See [CODEBASE_STRUCTURE.md](CODEBASE_STRUCTURE.md) for comprehensive folder organization and file placement guidelines.**

Key structural patterns:
- Services organized by domain: `Services/{Beaconing|Messaging|Packets|Ports|Platform|UI}/`
- Data layer separated: `Data/{Entities|Mappers|Converters|Migrations}/`
- APRS packets grouped: `Models/Aprs/Packets/`
- Factories categorized: `Factories/ViewModels/`
- Converters categorized: `Converters/{Aprs|UI}/`
- Namespace MUST match folder structure exactly

When adding new files, consult CODEBASE_STRUCTURE.md for proper placement and namespace conventions.

## Test Project

Tests use **xUnit v3** (`xunit.v3` package, **not** `xunit` v2). Important differences from v2:
- `[Fact]` is in `Xunit` namespace (not `Xunit.FactAttribute`)
- Test classes do NOT need `public` (but can be)
- Use `Assert.ThrowsAsync<T>` instead of `await Assert.ThrowsAsync<T>` with `.AsTask()` extension - `ValueTask`-returning methods work directly
- `IAsyncDisposable` types require `await using` (not `using`) in tests
- `TestContext.Current.CancellationToken` is available for test cancellation (suppresses xUnit1051 warnings)

No mocking library is currently referenced. When adding one, follow the central package management rules.

### Where to place tests

Mirror the source namespace structure under `AetherAprs.Tests/`:
- Source: `AetherAprs/Modems/Kiss/KissSerializer.cs` → Tests: `AetherAprs.Tests/Kiss/KissSerializerTests.cs`
- Namespace for tests: `AetherAprs.Tests.<Subnamespace>` (e.g. `AetherAprs.Tests.Kiss`)

## Architecture Overview

AetherAprs follows a clean two-flow architecture with clear separation between packet reception/persistence and packet querying/display.

### Data Flow: RECEIVE (Port → Database)

Incoming packets flow through a strictly enforced pipeline:

```
Port (RF/APRS-IS)
  ↓ receives packet, fires PacketReceived event
PortService (coordinates all ports)
  ↓ handles PacketReceived event
  ↓ calls PacketStorageService.StorePacketAsync()
PacketStorageService
  ↓ creates PacketRecord via PacketRecordMapper.ToRecord()
  ↓ saves to database via EF Core
SQLite Database
  ↓ PacketStored event raised after successful save
ViewModels subscribe to PacketStored event
```

**Key Rules:**
- Ports MUST NOT directly access the database
- Ports MUST NOT persist received data themselves
- PortService coordinates all port operations and delegates persistence to PacketStorageService
- PacketStorageService is the ONLY component that writes packet data to the database

### Data Flow: READ (Database → Consumers)

Database-backed packet information is accessed through the query service:

```
SQLite Database
  ↑ EF Core queries
PacketQueryService (IPacketQueryService)
  ↑ provides read-only query methods:
    - GetMostRecentPacketsAsync() - latest packet per callsign
    - GetMostRecentPositionPacketsAsync() - latest position per callsign  
    - GetPacketsByPortAsync() - packets from specific port
    - GetPacketsByCallsignAsync() - historical packets for trail display
  ↑
Consumers (ViewModels)
  - ReceivedBeaconsViewModel: queries for map display
  - PacketsViewModel: queries for packet list
  - PacketDetailsViewModel: queries for callsign history
```

**Key Rules:**
- ViewModels MUST NOT access EF Core or DbContext directly
- ViewModels MUST use PacketQueryService for all database reads
- Map display MUST obtain data via PacketQueryService (not direct database access)
- Packet details MUST obtain data via PacketQueryService (not direct database access)
- PacketQueryService provides read-only access - no data modification

### Data Flow: TRANSMIT (Consumer → Port)

Outgoing packets flow through PortService:

```
Consumer/ViewModel
  ↓ calls PortService.SendPacketAsync(portId, packet)
PortService
  ↓ routes to appropriate Port
Port (RF/APRS-IS)
  ↓ transmits packet
```

**Key Rules:**
- Consumers MUST use PortService.SendPacketAsync() for transmission
- Direct Port access for transmission is prohibited
- PortService handles port routing and coordination

### Architecture Boundaries - PROHIBITED

The following patterns violate the architecture and MUST NOT be used:

```
❌ ViewModel → EF Core (bypass query service)
❌ ViewModel → DbContext (bypass query service)
❌ Map → Database (bypass query service)
❌ PacketDetails → Database (bypass query service)
❌ Port → Database (bypass storage service)
❌ View → Database (views are presentation only)
❌ Consumer → Port directly (bypass port service)
```

### Service Responsibilities

**PortService** (`IPortService`):
- Manages port lifecycle (start, stop, add, remove, update)
- Receives packets from ports via event callbacks
- Delegates persistence to PacketStorageService
- Raises PacketReceived event for application-level consumers
- Handles outgoing transmission via SendPacketAsync()
- Coordinates digipeater service

**PacketStorageService** (`IPacketStorageService`):
- Writes received packets to database via EF Core
- Raises PacketStored event after successful storage
- Handles packet retention/cleanup based on configuration
- ONLY service that writes packet data

**PacketQueryService** (`IPacketQueryService`):
- Provides read-only database queries for packet data
- Aggregates data (most recent per callsign, etc.)
- Provides historical data for trails and details
- Used by ViewModels to obtain database-backed information

**MessageService** (`IMessageService`):
- Manages APRS messaging conversations
- Persists messages via EF Core (message-specific data)
- Coordinates message transmission via PortService
- Handles message acknowledgments and retries

**BeaconService** (`IBeaconService`):
- Manages beacon configurations
- Persists beacon configs via EF Core
- Coordinates beacon transmission via PortService

**DigipeaterService** (`IDigipeaterService`):
- Implements APRS digipeater functionality
- Coordinates with PortService for packet forwarding

## MVVM Architecture

AetherAprs uses Avalonia UI with the MVVM pattern via CommunityToolkit.Mvvm.

### ViewModels are UI-Layer Classes

**ViewModels represent UI state and user interaction.**

ViewModels should contain:
- UI state (IsLoading, IsEnabled, validation errors)
- User interaction commands ([RelayCommand])
- Presentation data (formatted strings, display collections)
- Orchestration of service calls
- UI-specific validation
- Navigation logic

**ViewModels are NOT general-purpose service classes.**

ViewModels should NOT contain:
- Database access logic (use PacketQueryService instead)
- Packet processing algorithms (belongs in services)
- Port management (belongs in PortService)
- Network operations (belongs in ports/transports)
- Business rule engines (belongs in services)
- Infrastructure logic (belongs in services)

**Correct ViewModel responsibility:**
```csharp
public partial class PacketsViewModel : ViewModelBase
{
    private readonly IPacketQueryService _packetQueryService;
    
    [ObservableProperty]
    public partial ObservableCollection<PacketSummary> Packets { get; set; }
    
    private async Task LoadPacketsAsync()
    {
        // ViewModel orchestrates service call and updates UI state
        IsLoading = true;
        var packets = await _packetQueryService.GetMostRecentPacketsAsync();
        Packets.Clear();
        foreach (var packet in packets.Values)
        {
            Packets.Add(CreateSummary(packet)); // UI presentation logic
        }
        IsLoading = false;
    }
}
```

**WRONG - ViewModel with business/database logic:**
```csharp
public partial class SomeViewModel : ViewModelBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory; // ❌ NO
    
    private async Task LoadDataAsync()
    {
        // ❌ ViewModels should not query database directly
        using var context = await _dbFactory.CreateDbContextAsync();
        var data = await context.Packets.Where(...).ToListAsync();
    }
}
```

### Views and ViewModels

**Every relevant View should have a corresponding ViewModel.**

- Page Views (HomeView, PacketsView, PortsView) → Page ViewModels
- Dialog Views (AddEditPortView, PacketDetailsView) → Dialog ViewModels  
- Component Views (LocationTrackingComponent, BeaconTransmissionComponent) → Component ViewModels

Simple presentational controls without logic may not need ViewModels.

**Dependency Injection**: `ServiceProviderFactory.CreateServiceProvider()` in `App.axaml.cs:OnFrameworkInitializationCompleted()` builds the DI container. Platform-specific services registered via `RegisterPlatformServices()` override (Android app provides its own `IAppDataDirProviderService`).

**Factory Pattern for Transient ViewModels**: When a ViewModel requires post-construction initialization with context-specific data, use the factory pattern instead of service locator:
- Create `IViewModelFactory` interface with `Create(...)` methods accepting context parameters
- Implementation uses `IServiceProvider.GetRequiredService<>()` to get transient ViewModel, calls `Initialize()`, returns initialized instance
- Register factory as singleton in both `ServiceProviderFactory.cs` and `DesignData.cs`
- Examples: `IAddEditPortViewModelFactory`, `IPacketDetailsViewModelFactory`, `IConversationViewModelFactory`

**MVVM**: Uses CommunityToolkit.Mvvm. ViewModels resolved from DI container and assigned to DataContext.

**ViewLocator Pattern**: `ViewLocator.cs` provides automatic ViewModel-to-View mapping. The ViewLocator is registered in `App.axaml` as an application-level DataTemplate.

When creating new ViewModels:
1. Register in `ServiceProviderFactory.cs` (runtime DI) - use appropriate lifetime (Singleton/Transient)
2. Register in `DesignData.cs` (design-time DI)
3. Add public property to expose the ViewModel instance in DesignData
4. Add ViewModel → View mapping in `ViewLocator.cs`

**ViewModel Lifetime Guidelines:**
- **Singleton**: Page-level ViewModels that persist across app lifetime (MainViewModel, HomeViewModel, PortsViewModel, etc.)
- **Transient**: Sub-components created on-demand (LocationTrackingViewModel, BeaconTransmissionViewModel, dialog ViewModels)
- **Transient with Factory**: ViewModels requiring post-construction initialization with context data (AddEditPortViewModel, PacketDetailsViewModel, ConversationViewModel)

**Child ViewModel Injection**: Parent ViewModels should inject child ViewModels via constructor, not create them directly:
```csharp
// CORRECT - Inject child ViewModel
public HomeViewModel(
    LocationTrackingViewModel locationTracking,
    MapViewModel map,
    ...)
{
    LocationTracking = locationTracking;
    Map = map;
}

// WRONG - Don't create child ViewModels directly
public SettingsViewModel(...)
{
    SymbolPicker = new AprsSymbolPickerViewModel(...); // Anti-pattern
}
```

When binding ViewModels to UI:
- ALWAYS use `<ContentControl Content="{Binding ViewModelProperty}" />`
- NEVER manually instantiate views with `<views:SomeView DataContext="{Binding ...}" />`
- NEVER use inline DataTemplates for ViewModel-to-View mapping
- Let the ViewLocator handle all ViewModel-to-View resolution automatically

Without proper registration in all three places (ServiceProviderFactory, DesignData, ViewLocator), views won't work at runtime or in the designer.

## Design-Time Support

**Design-time previewing is a project requirement.** Avalonia designer must be able to preview Views without runtime dependencies.

### Design-Time Architecture

- `DesignData.cs` provides a design-time service provider with mock/fake services
- All ViewModels MUST be registered in both `ServiceProviderFactory.cs` (runtime) and `DesignData.cs` (design-time)
- `ViewLocator.cs` maps ViewModels to Views for both runtime and design-time

### Design-Time Requirements

Design-time support MUST NOT require:
- Production database connections
- Live network ports (RF/APRS-IS)
- Network services
- Authentication
- File system access beyond app data directory
- Platform-specific APIs unavailable in the designer

### Providing Design-Time Data

Use fake/mock implementations in `DesignData.cs`:

```csharp
// Runtime uses real location service
services.AddSingleton<ILocationService, GeolocatorLocationService>();

// Design-time uses fake that doesn't require GPS hardware
services.AddSingleton<ILocationService, DesignTimeLocationService>();
```

Provide realistic preview data where useful:
```csharp
public class DesignTimeLocationService : ILocationService
{
    public Task<LocationData?> GetCurrentLocationAsync(CancellationToken ct = default)
    {
        // Realistic preview location
        return Task.FromResult<LocationData?>(new LocationData
        {
            Latitude = 37.7749,
            Longitude = -122.4194,
            Altitude = 50,
            Timestamp = DateTimeOffset.UtcNow
        });
    }
}
```

### Adding Design-Time Support to New Views

When creating a new View/ViewModel:
1. Register ViewModel in `ServiceProviderFactory.cs` with appropriate lifetime
2. Register ViewModel in `DesignData.cs` with the same lifetime
3. Add public property in `DesignData.cs` to expose the ViewModel instance
4. Add ViewModel → View mapping in `ViewLocator.cs`
5. Test the View in the Avalonia previewer

Without proper registration in all three places (ServiceProviderFactory, DesignData, ViewLocator), views won't work at runtime or in the designer.

**Configuration**: Uses Microsoft.Extensions.Configuration with `appsettings.json` and `appsettings.Development.json`. Files copied to output directory. Android project links these from core project via `<AndroidAsset Include="..\AetherAprs\appsettings.json">`. Development config only included in Android Debug builds.

**Multi-platform lifecycle**: App.axaml.cs handles three Avalonia lifetime types:
- `IClassicDesktopStyleApplicationLifetime` - Desktop (Windows, macOS, Linux)
- `IActivityApplicationLifetime` - Android/iOS with factory pattern
- `ISingleViewApplicationLifetime` - Browser/single-view platforms

## C# Naming Conventions

Follow these conventions consistently throughout the codebase:

**Fields:**
- Private instance fields MUST use `_camelCase`
  ```csharp
  private readonly ILogger _logger;
  ```
- Private static fields SHOULD use `_camelCase`
  ```csharp
  private static readonly Foo _instance;
  ```

**Properties and Methods:**
- Public and protected properties MUST use `PascalCase`
  ```csharp
  public AppSettings Settings { get; set; }
  ```
- Public and protected methods MUST use `PascalCase`
  ```csharp
  public void StartService()
  ```
- Private methods SHOULD use `PascalCase`
  ```csharp
  private void ValidateSettings()
  ```

**Variables and Parameters:**
- Method parameters MUST use `camelCase`
  ```csharp
  public void Configure(AppSettings settings)
  ```
- Local variables MUST use `camelCase`
  ```csharp
  var connectionString = ...;
  ```

**Constants and Types:**
- Constants SHOULD use `PascalCase`
  ```csharp
  private const int DefaultTimeout = 30;
  ```
- Types (classes, structs, interfaces, enums, records) MUST use `PascalCase`
  ```csharp
  public class ConfigurationService
  ```
- Interfaces MUST start with `I`
  ```csharp
  public interface IConfigurationService
  ```
- Enum members MUST use `PascalCase`
  ```csharp
  LogLevel.Warning
  ```

**MVVM Toolkit:**
- Use `[ObservableProperty]` on partial properties, NOT backing fields
  ```csharp
  // Correct
  [ObservableProperty]
  public partial string Title { get; set; } = "Default";

  // Incorrect - don't use backing field approach
  [ObservableProperty]
  private string _title = "Default";
  ```

**General Rules:**
- Do NOT use `this.` prefix when `_camelCase` fields distinguish fields from parameters
- The `_` prefix is specifically for fields. Do NOT prefix properties, methods, parameters, or local variables with `_`

## Memory Leak Prevention

**Event Handler Subscriptions**: Always unsubscribe from events to prevent memory leaks.

```csharp
// WRONG - Memory leak: subscription never cleaned up
public MyView()
{
    InitializeComponent();
    this.DataContextChanged += (s, e) =>
    {
        if (e.NewValue is MyViewModel vm)
        {
            vm.PropertyChanged += (s2, e2) => { /* ... */ };
        }
    };
}

// CORRECT - Track and unsubscribe
private MyViewModel? _currentViewModel;

private void OnDataContextChanged(object? sender, EventArgs e)
{
    if (_currentViewModel != null)
    {
        _currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    _currentViewModel = DataContext as MyViewModel;

    if (_currentViewModel != null)
    {
        _currentViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }
}

private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    // Handle property changes
}
```

**Self-Referencing PropertyChanged**: Never subscribe to your own PropertyChanged event with lambdas. Use CommunityToolkit.Mvvm partial methods instead.

```csharp
// WRONG - Memory leak: object holds reference to itself
public BeaconConfigurationItemViewModel()
{
    PropertyChanged += (s, e) =>
    {
        if (e.PropertyName == nameof(SlowIntervalSeconds))
        {
            UpdateConfiguration();
        }
    };
}

// CORRECT - Use partial methods
[ObservableProperty]
public partial int SlowIntervalSeconds { get; set; }

partial void OnSlowIntervalSecondsChanged(int value)
{
    UpdateConfiguration();
}
```

**Async Void Event Handlers**: Avoid `async void` except for event handlers, and add try-catch for error handling.

```csharp
// ACCEPTABLE with error handling
private async void OnSomeEvent(object? sender, EventArgs e)
{
    try
    {
        await DoSomethingAsync();
    }
    catch (Exception ex)
    {
        // Log error - exceptions in async void crash the app
        _logger?.LogError(ex, "Error in event handler");
    }
}

// BETTER - Use RelayCommand for user actions
[RelayCommand]
private async Task DoSomethingAsync()
{
    // Exceptions are captured by the command infrastructure
}
```

**Disposal and Cancellation**: Pass cancellation tokens to background tasks and cancel them during disposal.

```csharp
// WRONG - Fire-and-forget task continues after disposal
public async ValueTask DisposeAsync()
{
    // Task continues running
}

// CORRECT - Cancellation token stops task
private readonly CancellationTokenSource _disposalCts = new();

private void StartBackgroundWork()
{
    _ = Task.Run(async () =>
    {
        try
        {
            while (!_disposalCts.Token.IsCancellationRequested)
            {
                await DoWorkAsync(_disposalCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during disposal
        }
    }, _disposalCts.Token);
}

public async ValueTask DisposeAsync()
{
    _disposalCts.Cancel();
    _disposalCts.Dispose();
}
```

**Thread Safety in Disposal**: Capture references before nulling them when disposing from potentially different threads.

```csharp
// WRONG - Race condition
public void Dispose()
{
    _someLayer?.Dispose();
    _someLayer = null;
}

// CORRECT - Capture then null
public void Dispose()
{
    var layer = _someLayer;
    _someLayer = null;
    layer?.Dispose();
}
```

## Common Mistakes to Avoid

- Creating files without SPDX headers - pre-commit will reject
- Adding `using System;` style imports and assuming implicit usings - they're disabled
- Putting package versions in .csproj instead of Directory.Packages.props
- Assuming standard .NET namespaces are available without explicit using statements
- Adding Android-specific code to core project instead of Android project
- Creating new ViewModels without registering them in `ServiceProviderFactory.cs`, `DesignData.cs`, AND `ViewLocator.cs`
- Manually instantiating views or using inline DataTemplates instead of letting ViewLocator handle ViewModel-to-View resolution
- Using inconsistent naming conventions for private fields (always use `_camelCase`)
- Using backing field approach with `[ObservableProperty]` instead of partial properties
- Subscribing to events without unsubscribing (memory leaks)
- Using lambda subscriptions to own PropertyChanged event (memory leaks)
- Fire-and-forget async operations without cancellation tokens or error handling
- Missing try-catch in async void event handlers (app crashes on exception)
- Using service locator pattern (`App.GetService<>()` or `IServiceProvider.GetRequiredService<>()`) instead of constructor injection or factory pattern
- Creating child ViewModels directly instead of injecting them via constructor
- Using `System.Diagnostics.Debug.WriteLine` instead of `ILogger<T>` for error logging
- **Adding hardcoded user-facing text in AXAML or code without using localization** - ALL user-facing strings MUST use `{loc:Loc StringKey}` in AXAML or `Strings.Get("StringKey")` in code
- **Placing files in wrong folders** - consult CODEBASE_STRUCTURE.md for proper organization
- **Using wrong namespace** - namespace MUST match folder structure exactly (e.g., `Data/Entities/` → `AetherAprs.Data.Entities`)

## Database Best Practices

### SQLite and EF Core Compatibility

**Use `DateTime` (UTC) instead of `DateTimeOffset` for database timestamps:**
- SQLite stores both as TEXT in ISO 8601 format, but EF Core has poor LINQ translation support for `DateTimeOffset`
- `DateTimeOffset` comparisons and ORDER BY operations often fail to translate to SQL
- Store timestamps as `DateTime` in UTC, convert to `DateTimeOffset` for UI display when needed
- Example: `record.ReceivedAt = receivedAt.UtcDateTime;` when storing, `new DateTimeOffset(record.ReceivedAt, TimeSpan.Zero)` when displaying

## Localization Requirements

**ALL user-facing text MUST be localized.** Never hardcode English (or any language) strings in UI code or AXAML files.

### In AXAML Files
Use the `{loc:Loc}` markup extension:
```xml
<TextBlock Text="{loc:Loc SettingsPageTitle}" />
<CheckBox Content="{loc:Loc EnableDigipeater}" />
<TextBox wpf:TextFieldAssist.Label="{loc:Loc PortName}" />
```

### In C# Code
Use `Strings.Get()` or `Strings.Format()`:
```csharp
// Simple string
var title = Strings.Get("SettingsPageTitle");

// Formatted string with parameters
var message = Strings.Format("ErrorConnecting", portName);
```

### Adding New Strings
When adding new UI text:

1. **Add to `AetherAprs/Localization/Strings.resx`** (English, default):
```xml
<data name="EnableDigipeater" xml:space="preserve">
  <value>Enable Digipeater</value>
</data>
```

2. **Add to `AetherAprs/Localization/Strings.pt.resx`** (Portuguese translation):
```xml
<data name="EnableDigipeater" xml:space="preserve">
  <value>Ativar Digipeater</value>
</data>
```

3. Use consistent naming conventions:
   - Page titles: `PageNameTitle` (e.g., `SettingsPageTitle`)
   - Section titles: `SectionName` (e.g., `StationSettings`, `DigipeaterAndGating`)
   - Field labels: descriptive names (e.g., `EnableDigipeater`, `MaximumRetryAttempts`)
   - Descriptions: append `Description` (e.g., `DigipeaterGatingDescription`)
   - Placeholders: append `Placeholder` (e.g., `CallsignPlaceholder`)

### When NOT to Localize
- Log messages (use English for consistency in logs)
- Exception messages in code (use English)
- Developer comments
- Configuration keys
- Technical identifiers

## Async/Await Best Practices

**Fire-and-Forget Pattern**: Only acceptable when properly wrapped with error handling and cancellation:
```csharp
// ACCEPTABLE - Fire-and-forget with error recovery
private async Task HandleToggleAsync()
{
    try
    {
        await _onToggle(this);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Error toggling port {PortName}", Name);
        // Revert UI state on failure
        _isInitializing = true;
        IsEnabled = !IsEnabled;
        _isInitializing = false;
    }
}

partial void OnIsEnabledChanged(bool value)
{
    if (!_isInitializing)
        _ = HandleToggleAsync(); // Fire-and-forget with error handling
}
```

**Async Callbacks**: When property change handlers need to call async methods, use async callbacks:
```csharp
// Callback signature
private readonly Func<PortItemViewModel, Task> _onToggle;

// Usage in parent
private async Task OnTogglePortAsync(PortItemViewModel item)
{
    await _portService.SetPortEnabledAsync(item.Id, item.IsEnabled);
}
```

**Logging Best Practices**: Use structured logging with `ILogger<T>` consistently:
```csharp
// CORRECT - Structured logging
_logger?.LogError(ex, "Error toggling port {PortName} (ID: {PortId})", Name, Id);

// WRONG - Debug.WriteLine (inconsistent, no structure)
System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
```
