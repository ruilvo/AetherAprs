<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Developer Guide

This guide explains AetherAprs architecture, conventions, and development workflows.

## Architecture Overview

AetherAprs follows a clean two-flow architecture with strict separation between packet persistence and querying.

### Technology Stack

- **UI Framework**: Avalonia UI 11 with Material Design
- **MVVM**: CommunityToolkit.Mvvm for [ObservableProperty] and [RelayCommand]
- **Database**: SQLite with Entity Framework Core 10
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Configuration**: Microsoft.Extensions.Configuration (appsettings.json)
- **Mapping**: Mapsui library for OpenStreetMap display
- **Logging**: Microsoft.Extensions.Logging (Debug + Console providers)
- **Testing**: xUnit v3 with NSubstitute

### Data Flow Architecture

#### RECEIVE Flow (Port → Database)

Incoming packets follow a strictly enforced pipeline:

```
Port (RF/APRS-IS)
  ↓ receives packet, fires PacketReceived event
PortService (IPortService)
  ↓ handles event, coordinates persistence
  ↓ calls PacketStorageService.StorePacketAsync()
PacketStorageService (IPacketStorageService)
  ↓ maps AprsPacket → PacketRecord
  ↓ saves via EF Core
SQLite Database
  ↓ PacketStored event raised
ViewModels subscribe to PacketStored event
```

**Architectural Rules:**
- **Ports** handle transport/protocol only (RF, APRS-IS, KISS)
- Ports MUST NOT persist data themselves
- **PortService** coordinates all ports and delegates persistence
- **PacketStorageService** is the ONLY component that writes packet data
- Database writes happen exclusively through PacketStorageService

#### READ Flow (Database → UI)

Database queries flow through the query service:

```
SQLite Database
  ↑ EF Core LINQ queries
PacketQueryService (IPacketQueryService)
  ↑ read-only query methods:
    - GetMostRecentPacketsAsync() - latest per callsign
    - GetMostRecentPositionPacketsAsync() - latest position per callsign
    - GetPacketsByPortAsync(portId) - packets from specific port
    - GetPacketsByCallsignAsync(callsign) - historical packets for trails
  ↑
ViewModels and Services (UI layer)
  - ReceivedBeaconsMapLayerService: provides map layers for display
  - PacketsViewModel: packet list UI
  - PacketDetailsViewModel: callsign history UI
  ↑
Views (AXAML)
```

**Architectural Rules:**
- **ViewModels** are UI-layer classes (not general services)
- ViewModels MUST use PacketQueryService for database reads
- **Services** MAY use PacketQueryService for business logic requiring packet data
- ViewModels MUST NOT access EF Core or DbContext directly
- **Map display** obtains data via ReceivedBeaconsMapLayerService (which uses PacketQueryService)
- **Packet details** obtains data via PacketQueryService
- PacketQueryService provides read-only access

#### TRANSMIT Flow (UI → Port)

Outgoing packets flow through PortService:

```
ViewModel/Service
  ↓ calls PortService.SendPacketAsync(portId, packet)
PortService
  ↓ routes to appropriate port
Port (RF/APRS-IS)
  ↓ transmits via modem/network
```

**Architectural Rules:**
- All transmission goes through PortService.SendPacketAsync()
- Direct port access for transmission is prohibited
- PortService handles port routing and error handling

### Prohibited Architecture Patterns

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
- Receives packets from ports via PacketReceived event callbacks
- Delegates packet persistence to PacketStorageService
- Raises PacketReceived event for application-level consumers
- Handles outgoing transmission via SendPacketAsync(portId, packet)
- Coordinates digipeater service for packet forwarding
- Provides port configuration and status

**PacketStorageService** (`IPacketStorageService`):
- Writes received packets to database via EF Core
- Maps AprsPacket domain models to PacketRecord database entities
- Raises PacketStored event after successful storage
- Handles packet retention and cleanup based on configuration
- ONLY service that writes packet data to database

**PacketQueryService** (`IPacketQueryService`):
- Provides read-only database queries for packet data
- Aggregates data (most recent per callsign, position packets only, etc.)
- Provides historical data for trail display and packet details
- Used by ViewModels to obtain database-backed information
- Never modifies data (read-only interface)

**MessageService** (`IMessageService`):
- Manages APRS messaging conversations
- Persists messages via EF Core (message-specific data model)
- Coordinates message transmission via PortService
- Handles message acknowledgments (ACK/REJ)
- Implements retry logic with exponential backoff
- Maintains conversation threads

**BeaconService** (`IBeaconService`):
- Manages beacon configurations (Walk/Drive/Custom presets)
- Persists beacon configs via EF Core
- Coordinates beacon transmission via PortService
- Implements smart beaconing logic (speed/course/distance triggers)

**DigipeaterService** (`IDigipeaterService`):
- Implements APRS digipeater functionality
- Coordinates with PortService for packet forwarding
- Handles RF-to-RF and RF-to-APRS-IS gating
- Applies WIDEn-N path processing
- Inserts callsign into digipeated paths (optional)

## MVVM Architecture

### ViewModels are UI-Layer Classes

**ViewModels represent UI state and user interaction, not business logic.**

ViewModels should contain:
- UI state (IsLoading, IsEnabled, error messages, empty states)
- User interaction commands ([RelayCommand], [AsyncRelayCommand])
- Presentation data (formatted strings, display collections)
- Orchestration of service calls
- UI-specific validation
- Navigation logic

ViewModels should NOT contain:
- Database access logic (use PacketQueryService)
- Packet processing algorithms (belongs in services)
- Port management (belongs in PortService)
- Business rule engines (belongs in services)
- Infrastructure logic (belongs in services/utilities)

**Example - Correct ViewModel:**
```csharp
public partial class PacketsViewModel : ViewModelBase, IDisposable
{
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly INavigationService _navigationService;
    
    [ObservableProperty]
    public partial ObservableCollection<PacketSummary> Packets { get; set; } = [];
    
    [ObservableProperty]
    public partial bool IsLoading { get; set; }
    
    public PacketsViewModel(
        IPacketQueryService packetQueryService,
        IPacketStorageService packetStorageService,
        INavigationService navigationService)
    {
        _packetQueryService = packetQueryService;
        _packetStorageService = packetStorageService;
        _navigationService = navigationService;
        
        _packetStorageService.PacketStored += OnPacketStored;
        _ = LoadPacketsAsync();
    }
    
    private void OnPacketStored(object? sender, EventArgs e)
    {
        // React to new packets
        _ = LoadPacketsAsync();
    }
    
    private async Task LoadPacketsAsync()
    {
        IsLoading = true;
        
        // ViewModel orchestrates service call
        var packets = await _packetQueryService.GetMostRecentPacketsAsync();
        
        // ViewModel handles UI presentation
        Packets.Clear();
        foreach (var packet in packets.Values)
        {
            Packets.Add(CreateSummary(packet));
        }
        
        IsLoading = false;
    }
    
    [RelayCommand]
    private void ViewPacketDetails(PacketSummary summary)
    {
        // ViewModel handles navigation
        var vm = _packetDetailsFactory.Create(summary.Source);
        _navigationService.NavigateTo(vm);
    }
}
```

**Example - WRONG (ViewModel with database logic):**
```csharp
public partial class BadViewModel : ViewModelBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory; // ❌ NO
    
    private async Task LoadDataAsync()
    {
        // ❌ ViewModels should NOT query database directly
        using var context = await _dbFactory.CreateDbContextAsync();
        var packets = await context.Packets
            .Where(p => p.ReceivedAt > DateTime.UtcNow.AddDays(-1))
            .OrderByDescending(p => p.Id)
            .ToListAsync();
            
        // ❌ This database logic belongs in PacketQueryService
    }
}
```

### Views and ViewModels

**Every relevant View should have a corresponding ViewModel.**

- **Page Views** (HomeView, PacketsView, PortsView) → Page ViewModels
- **Dialog Views** (AddEditPortView, PacketDetailsView) → Dialog ViewModels
- **Component Views** (LocationTrackingComponent, BeaconTransmissionComponent) → Component ViewModels

Simple presentational controls without logic may not need ViewModels.

### ViewLocator Pattern

AetherAprs uses automatic ViewModel-to-View mapping via `ViewLocator.cs`:

```csharp
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;
        
        var name = data.GetType().FullName!.Replace("ViewModel", "View");
        var type = Type.GetType(name);
        
        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }
        
        return new TextBlock { Text = "View Not Found: " + name };
    }
    
    public bool Match(object? data) => data is ViewModelBase;
}
```

**When creating a new View/ViewModel:**
1. Name ViewModel: `FooViewModel`
2. Name View: `FooView` (in same namespace)
3. Register ViewModel in `ServiceProviderFactory.cs` (runtime DI)
4. Register ViewModel in `DesignData.cs` (design-time DI)
5. Add ViewModel → View mapping in `ViewLocator.cs` if non-standard naming
6. Test in Avalonia previewer

### Dependency Injection

AetherAprs uses Microsoft.Extensions.DependencyInjection.

**Service registration:**
- `ServiceProviderFactory.cs` - Runtime service provider
- `DesignData.cs` - Design-time service provider for Avalonia previewer

**ViewModel lifetimes:**
- **Singleton**: Page-level ViewModels (HomeViewModel, PacketsViewModel, PortsViewModel, SettingsViewModel, MessagesViewModel)
- **Transient**: Component ViewModels (LocationTrackingViewModel, BeaconTransmissionViewModel, AprsSymbolPickerViewModel)
- **Transient with Factory**: ViewModels requiring post-construction initialization (AddEditPortViewModel, PacketDetailsViewModel, ConversationViewModel)

**Factory pattern for transient ViewModels:**

When a ViewModel requires initialization with context-specific data after construction, use the factory pattern:

```csharp
public interface IPacketDetailsViewModelFactory
{
    PacketDetailsViewModel Create(string callsign);
}

public class PacketDetailsViewModelFactory : IPacketDetailsViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public PacketDetailsViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public PacketDetailsViewModel Create(string callsign)
    {
        var vm = _serviceProvider.GetRequiredService<PacketDetailsViewModel>();
        vm.Initialize(callsign);
        return vm;
    }
}

// Register factory as singleton
services.AddSingleton<IPacketDetailsViewModelFactory, PacketDetailsViewModelFactory>();
```

### Child ViewModel Injection

Parent ViewModels should inject child ViewModels via constructor:

```csharp
// CORRECT - Inject child ViewModels
public HomeViewModel(
    LocationTrackingViewModel locationTracking,
    BeaconTransmissionViewModel beaconTransmission,
    MapViewModel map,
    ReceivedBeaconsViewModel receivedBeacons,
    ...)
{
    LocationTracking = locationTracking;
    BeaconTransmission = beaconTransmission;
    Map = map;
    ReceivedBeacons = receivedBeacons;
}

// WRONG - Create child ViewModels directly
public SettingsViewModel(...)
{
    SymbolPicker = new AprsSymbolPickerViewModel(...); // ❌ Anti-pattern
}
```

## Design-Time Support

**Design-time previewing is a project requirement.** The Avalonia previewer must work without runtime dependencies.

### Design-Time Architecture

- `DesignData.cs` provides a design-time service provider
- All ViewModels registered in both runtime and design-time providers
- Mock/fake services used for design-time to avoid external dependencies

### Design-Time Requirements

Design-time support MUST NOT require:
- Production database connections
- Live network ports (RF/APRS-IS)
- Network services or APIs
- Authentication systems
- GPS hardware
- Bluetooth hardware
- Platform-specific APIs unavailable in designer

### Providing Design-Time Data

Use fake implementations in `DesignData.cs`:

```csharp
public static class DesignData
{
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        
        // Design-time uses fake location service (no GPS required)
        services.AddSingleton<ILocationService, DesignTimeLocationService>();
        
        // Design-time uses in-memory database
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseInMemoryDatabase("DesignTime"));
        
        // ... register all ViewModels same as runtime
        
        return services.BuildServiceProvider();
    }
}

public class DesignTimeLocationService : ILocationService
{
    public Task<LocationData?> GetCurrentLocationAsync(CancellationToken ct = default)
    {
        // Provide realistic preview data
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

## Localization

**ALL user-facing text MUST be localized.** Never hardcode strings in UI code or AXAML files.

### Localization System

- Resource files: `AetherAprs/Localization/Strings.resx` (English), `Strings.pt.resx` (Portuguese)
- AXAML: Use `{loc:Loc StringKey}` markup extension
- C#: Use `Strings.Get("StringKey")` or `Strings.Format("StringKey", args)`

### What Must Be Localized

- Button labels, headings, descriptions
- Placeholders, hints, tooltips
- Validation messages, errors, warnings
- Dialog text, notifications, status messages
- Empty states, loading states
- Accessibility text
- Dynamically generated user-facing text

### What Should NOT Be Localized

- Log messages (use English for consistency)
- Exception messages in code
- Developer comments
- Configuration keys
- Technical identifiers (callsigns, frequencies, etc.)

### Adding New Localized Strings

1. Add to `Strings.resx` (English):
```xml
<data name="EnableDigipeater" xml:space="preserve">
  <value>Enable Digipeater</value>
</data>
```

2. Add to `Strings.pt.resx` (Portuguese):
```xml
<data name="EnableDigipeater" xml:space="preserve">
  <value>Ativar Digipeater</value>
</data>
```

3. Use in AXAML:
```xml
<CheckBox Content="{loc:Loc EnableDigipeater}" />
```

4. Use in C#:
```csharp
var label = Strings.Get("EnableDigipeater");
var message = Strings.Format("ErrorConnecting", portName);
```

### Naming Conventions

- Page titles: `PageNameTitle` (e.g., `SettingsPageTitle`)
- Section titles: `SectionName` (e.g., `StationSettings`)
- Field labels: descriptive names (e.g., `EnableDigipeater`)
- Descriptions: append `Description` (e.g., `DigipeaterDescription`)
- Placeholders: append `Placeholder` (e.g., `CallsignPlaceholder`)

## Development Workflow

### Prerequisites

- .NET 10 SDK
- Android workload: `dotnet workload install android`
- Git with pre-commit hooks configured
- IDE: Visual Studio 2022, Rider, or VS Code with C# Dev Kit

### Building

```powershell
# Build entire solution
dotnet build AetherAprs.slnx

# Build specific project
dotnet build AetherAprs/AetherAprs.csproj
dotnet build AetherAprs.Android/AetherAprs.Android.csproj

# Run desktop (if supported on platform)
dotnet run --project AetherAprs/AetherAprs.csproj
```

### Testing

```powershell
# Run all tests
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj

# Run tests without rebuilding
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj --no-build

# Run specific test
dotnet test --filter "FullyQualifiedName~PacketsViewModelTests"
```

### License Compliance

Every file MUST have an SPDX license header. Pre-commit hook enforces this via `reuse-lint-file`.

**C# files:**
```csharp
// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later
```

**AXAML/XML files:**
```xml
<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: GPL-3.0-or-later
-->
```

**Verify compliance:**
```powershell
pre-commit run reuse-lint-file --all-files
```

### Package Management

Versions centralized in `Directory.Packages.props`.

**Adding a package:**
1. Add version to `Directory.Packages.props`:
   ```xml
   <PackageVersion Include="PackageName" Version="x.y.z" />
   ```
2. Reference in `.csproj` (no version):
   ```xml
   <PackageReference Include="PackageName" />
   ```

### Code Conventions

See [`AGENTS.md`](../../AGENTS.md) for comprehensive coding conventions including:
- C# naming conventions
- MVVM patterns with CommunityToolkit.Mvvm
- Memory leak prevention
- Async/await best practices
- Dependency injection patterns

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Ensure all files have SPDX headers (pre-commit hook enforces)
4. Follow architecture boundaries (no ViewModels accessing EF Core directly)
5. Follow MVVM patterns (ViewModels are UI-layer classes)
6. Localize all user-facing text
7. Add tests for new features
8. Run `dotnet build` and `dotnet test` before committing
9. Update documentation if adding features or changing architecture
10. Commit: `git commit -m 'Add amazing feature'`
11. Push: `git push origin feature/amazing-feature`
12. Open a Pull Request

## Additional Resources

- [`AGENTS.md`](../../AGENTS.md) - Comprehensive AI agent instructions and coding conventions
- [`CLAUDE.md`](../../CLAUDE.md) - LLM-specific development guidelines
- [Avalonia Documentation](https://docs.avaloniaui.net/)
- [CommunityToolkit.Mvvm Documentation](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/)
- [APRS Protocol Specification](http://www.aprs.org/doc/APRS101.PDF)
