<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# Codebase Structure

This document describes the semantic organization of the AetherAprs codebase.

## Project Overview

```
AetherAprs/                 # Core cross-platform library (net10.0)
AetherAprs.Android/         # Android application (net10.0-android)
AetherAprs.Tests/           # Test project (net10.0)
```

## Core Project Structure (AetherAprs/)

### Configuration/
Application and port configuration types.

```
Configuration/
├── Settings/               # Application settings classes
│   ├── AppSettings.cs     # Root settings container
│   └── AprsSettings.cs    # APRS-specific settings
├── PortConfig.cs          # Base port configuration
├── KissTransportSettings.cs  # KISS transport configurations
└── PacketDisplayTimeRange.cs # UI time range enum
```

**Placement rules:**
- Settings classes → `Configuration/Settings/`
- Port and transport configs → `Configuration/`
- UI configuration enums → `Configuration/`

### Converters/
Avalonia value converters for data binding.

```
Converters/
├── Aprs/                  # APRS-specific converters
│   └── AprsSsidConverter.cs
└── UI/                    # General UI converters
    ├── BoolToTrackingStatusConverter.cs
    ├── EnumDisplayConverter.cs
    ├── EnumToBoolConverter.cs
    ├── PacketDisplayTimeRangeConverter.cs
    └── TypeNameToIsVisibleConverter.cs
```

**Placement rules:**
- APRS protocol converters → `Converters/Aprs/`
- General UI converters → `Converters/UI/`

### Data/
Database entities, mappers, and EF Core infrastructure.

```
Data/
├── Entities/              # EF Core entity classes
│   ├── PortRecord.cs
│   ├── PacketRecord.cs
│   ├── BeaconConfigRecord.cs
│   └── BeaconingStateRecord.cs
├── Mappers/               # Domain ↔ Entity mappers
│   ├── PortRecordMapper.cs
│   ├── PacketRecordMapper.cs
│   └── BeaconConfigMapper.cs
├── Converters/            # EF Core value converters
│   └── CoordinateValueConverter.cs
├── Migrations/            # EF Core migrations
│   ├── {timestamp}_*.cs
│   ├── {timestamp}_*.Designer.cs
│   └── AppDbContextModelSnapshot.cs
├── AppDbContext.cs        # EF Core context
├── AppDbContextFactory.cs # Context factory
├── AppSavedDataInitializer.cs  # Database initialization
└── SavedDataJson.cs       # JSON data transfer
```

**Placement rules:**
- Database entity classes → `Data/Entities/`
- Domain-to-entity mappers → `Data/Mappers/`
- EF Core value converters → `Data/Converters/`
- EF Core migrations → `Data/Migrations/`
- DbContext and infrastructure → `Data/` root

**Namespace convention:**
- Entities: `AetherAprs.Data.Entities`
- Mappers: `AetherAprs.Data.Mappers`
- Converters: `AetherAprs.Data.Converters`
- Migrations: `AetherAprs.Data.Migrations`

### Factories/
Factory patterns for creating complex objects.

```
Factories/
├── ViewModels/            # ViewModel factories
│   ├── IAddEditPortViewModelFactory.cs
│   ├── AddEditPortViewModelFactory.cs
│   ├── IConversationViewModelFactory.cs
│   ├── ConversationViewModelFactory.cs
│   ├── IPacketDetailsViewModelFactory.cs
│   └── PacketDetailsViewModelFactory.cs
└── ServiceProviderFactory.cs  # DI container factory
```

**Placement rules:**
- ViewModel factories → `Factories/ViewModels/`
- Service provider factory → `Factories/` root

**Namespace convention:**
- ViewModel factories: `AetherAprs.Factories.ViewModels`

### Models/
Domain models and value objects.

```
Models/
├── Aprs/                  # APRS protocol models
│   ├── Packets/           # Packet type hierarchy
│   │   ├── AprsPacket.cs (base)
│   │   ├── PositionPacket.cs
│   │   ├── MessagePacket.cs
│   │   ├── MessageAckPacket.cs
│   │   ├── MessageRejPacket.cs
│   │   ├── StatusPacket.cs
│   │   ├── WeatherPacket.cs
│   │   ├── TelemetryPacket.cs
│   │   ├── CapabilitiesPacket.cs
│   │   └── UnknownPacket.cs
│   ├── Callsign.cs        # Value types
│   ├── Symbol.cs
│   ├── SymbolCharacter.cs
│   ├── AprsSsid.cs        # Enums
│   ├── SymbolTable.cs
│   ├── SymbolCode.cs
│   └── PathHelper.cs      # Helper utilities
├── Messaging/             # Messaging domain
│   ├── StoredMessage.cs
│   ├── ConversationThread.cs
│   └── MessageDeliveryStatus.cs
├── BeaconConfig.cs        # Beacon configuration
├── DynamicBeaconMode.cs   # Beacon mode enum
└── LocationData.cs        # Location data
```

**Placement rules:**
- APRS packet types → `Models/Aprs/Packets/`
- APRS value types/enums → `Models/Aprs/`
- Messaging models → `Models/Messaging/`
- Other domain models → `Models/` root

**Namespace convention:**
- Packet types: `AetherAprs.Models.Aprs.Packets`
- APRS domain: `AetherAprs.Models.Aprs`
- Messaging: `AetherAprs.Models.Messaging`

### Modems/
Protocol modem implementations.

```
Modems/
├── Aprs/                  # APRS modems
│   ├── IAprsModem.cs
│   ├── AprsIsModem.cs     # APRS-IS client
│   ├── AprsRfModem.cs     # RF modem
│   ├── AprsInfoFieldParser.cs
│   ├── AprsInfoFieldSerializer.cs
│   ├── Ax25Parser.cs
│   └── Ax25Serializer.cs
└── Kiss/                  # KISS protocol
    ├── KissModem.cs
    ├── KissSerializer.cs
    ├── KissFrame.cs
    ├── KissCommandType.cs
    └── KissConstants.cs
```

**Placement rules:**
- APRS protocol implementation → `Modems/Aprs/`
- KISS protocol implementation → `Modems/Kiss/`

### Services/
Application services organized by domain.

```
Services/
├── Beaconing/             # Beacon transmission
│   ├── IBeaconService.cs
│   └── BeaconService.cs
├── Bluetooth/             # Bluetooth device discovery
│   ├── IBluetoothLeScanner.cs
│   ├── IBluetoothClassicDeviceProvider.cs
│   ├── UnsupportedBluetoothLeScanner.cs
│   ├── UnsupportedBluetoothClassicDeviceProvider.cs
│   └── BluetoothDevices.cs  # DTOs
├── Configuration/         # Application configuration
│   ├── IConfigurationService.cs
│   └── ConfigurationService.cs
├── Contracts/             # Shared service DTOs
│   └── PortPacketReceivedEventArgs.cs
├── Location/              # Location tracking
│   ├── ILocationTrackingService.cs
│   └── LocationTrackingService.cs
├── Messaging/             # APRS messaging
│   ├── IMessageService.cs
│   └── MessageService.cs
├── Packets/               # Packet storage and querying
│   ├── IPacketQueryService.cs
│   ├── PacketQueryService.cs
│   └── PacketStorageService.cs
├── Platform/              # Platform abstraction
│   ├── IAppDataDirProviderService.cs
│   ├── AppDataDirProviderService.cs
│   ├── IForegroundService.cs
│   ├── NoOpForegroundService.cs
│   ├── ILocationService.cs
│   ├── NoOpLocationService.cs
│   ├── IPermissionService.cs
│   └── NoOpPermissionService.cs
├── Ports/                 # Port management
│   ├── IPortService.cs
│   ├── PortService.cs
│   ├── IDigipeaterService.cs
│   ├── DigipeaterService.cs
│   └── AprsPortSettingsResolver.cs
├── Transmission/          # Beacon transmission coordination
│   ├── IBeaconTransmissionService.cs
│   └── BeaconTransmissionService.cs
└── UI/                    # UI services
    ├── INavigationService.cs
    ├── NavigationService.cs
    ├── IUiCultureProvider.cs
    ├── OsUiCultureProvider.cs
    ├── IKeyboardInsetsService.cs
    ├── KeyboardInsetsService.cs
    ├── IReceivedBeaconsMapLayerService.cs
    ├── ReceivedBeaconsMapLayerService.cs
    ├── IUserLocationLayerService.cs
    └── UserLocationLayerService.cs
```

**Placement rules:**
- Beacon services → `Services/Beaconing/`
- Bluetooth device services → `Services/Bluetooth/`
- Configuration services → `Services/Configuration/`
- Shared service DTOs → `Services/Contracts/`
- Location tracking → `Services/Location/`
- Messaging services → `Services/Messaging/`
- Packet services → `Services/Packets/`
- Platform abstraction (permissions, foreground service, location, app data) → `Services/Platform/`
- Port management → `Services/Ports/`
- Beacon transmission coordination → `Services/Transmission/`
- UI services (navigation, map layers, keyboard insets, culture) → `Services/UI/`

**Namespace convention:**
- Each subfolder maps to: `AetherAprs.Services.{Subfolder}`

### Transports/
Network transport implementations.

```
Transports/
└── Kiss/                  # KISS stream connectors
    ├── IKissStreamFactory.cs
    ├── KissStreamFactory.cs
    ├── IKissStreamConnector.cs
    ├── TcpKissStreamConnector.cs
    ├── UnsupportedBluetoothClassicKissStreamConnector.cs
    └── UnsupportedBluetoothLeKissStreamConnector.cs
```

**Placement rules:**
- KISS transport implementations → `Transports/Kiss/`

### ViewModels/ and Views/
MVVM view layer organized by pages and components.

```
ViewModels/
├── Components/            # Reusable component ViewModels
│   ├── AprsSymbolPickerViewModel.cs
│   ├── MapViewModel.cs
│   ├── PortItemViewModel.cs
│   └── SymbolSelectorViewModel.cs
├── Pages/                 # Page-level ViewModels
│   ├── AddEditPortViewModel.cs
│   ├── ConversationViewModel.cs
│   ├── DynamicBeaconingViewModel.cs
│   ├── HomeViewModel.cs
│   ├── MessagesViewModel.cs
│   ├── PacketDetailsViewModel.cs
│   ├── PacketsViewModel.cs
│   ├── PortsViewModel.cs
│   └── SettingsViewModel.cs
├── MainViewModel.cs
└── ViewModelBase.cs

Views/
├── Components/            # Reusable component Views
│   ├── AprsSymbolPickerComponent.axaml[.cs]
│   ├── PortItemComponent.axaml[.cs]
│   ├── ScrollablePageContent.axaml[.cs]
│   └── SymbolSelectorComponent.axaml[.cs]
├── Pages/                 # Page-level Views
│   ├── AddEditPortPage.axaml[.cs]
│   ├── ConversationPage.axaml[.cs]
│   ├── DynamicBeaconingPage.axaml[.cs]
│   ├── HomePage.axaml[.cs]
│   ├── MessagesPage.axaml[.cs]
│   ├── PacketDetailsPage.axaml[.cs]
│   ├── PacketsPage.axaml[.cs]
│   ├── PortsPage.axaml[.cs]
│   └── SettingsPage.axaml[.cs]
├── Windows/
│   └── MainWindow.axaml[.cs]
└── MainView.axaml[.cs]
```

**Placement rules:**
- Page ViewModels → `ViewModels/Pages/`
- Component ViewModels → `ViewModels/Components/`
- Top-level ViewModels → `ViewModels/` root
- Page Views → `Views/Pages/`
- Component Views → `Views/Components/`

**Note:** Location tracking, beacon transmission, and map layer management are implemented as services (in `Services/Location/`, `Services/Transmission/`, `Services/UI/`), not ViewModels.

## Android Project Structure (AetherAprs.Android/)

```
AetherAprs.Android/
├── Services/
│   ├── Bluetooth/         # Android Bluetooth implementation
│   │   ├── AndroidBluetoothClassicDeviceProvider.cs
│   │   ├── AndroidBluetoothLeScanner.cs
│   │   └── BluetoothPermissionHelper.cs
│   ├── Platform/          # Android platform services
│   │   ├── AndroidForegroundService.cs
│   │   ├── AndroidUiCultureProvider.cs
│   │   ├── AppDataDirProviderService.cs
│   │   └── LocationService.cs
│   └── Transports/        # Android transport implementations
│       ├── BluetoothClassicKissStreamConnector.cs
│       ├── BluetoothLeKissStreamConnector.cs
│       ├── BluetoothLeGattDuplexStream.cs
│       └── BluetoothSocketDuplexStream.cs
├── Application.cs
└── MainActivity.cs
```

**Namespace convention:**
- Bluetooth: `AetherAprs.Android.Services.Bluetooth`
- Platform: `AetherAprs.Android.Services.Platform`
- Transports: `AetherAprs.Android.Services.Transports`

## Test Project Structure (AetherAprs.Tests/)

Tests mirror the source structure and follow the same organizational principles:

```
AetherAprs.Tests/
├── Services/              # Service tests
│   ├── Beaconing/         # Beacon service tests
│   │   └── BeaconServiceTests.cs
│   ├── Bluetooth/         # Bluetooth service tests
│   │   └── UnsupportedBluetoothStubTests.cs
│   ├── Configuration/     # Configuration service tests
│   │   └── ConfigurationServiceTests.cs
│   ├── Messaging/         # Messaging service tests
│   │   └── MessageServiceTests.cs
│   ├── Platform/          # Platform service tests
│   │   └── AppDataDirProviderServiceTests.cs
│   ├── Ports/             # Port service tests
│   │   ├── AprsPortSettingsResolverTests.cs
│   │   └── PortServiceTests.cs
│   └── UI/                # UI service tests
│       └── NavigationServiceTests.cs
├── Modems/                # Modem tests
│   ├── Aprs/              # APRS modem tests
│   │   ├── AprsIsModemTests.cs
│   │   ├── AprsParserTests.cs
│   │   ├── AprsRfModemTests.cs
│   │   ├── AprsSerializerTests.cs
│   │   ├── Ax25SerializerPathTests.cs
│   │   ├── MicEParsingTests.cs
│   │   ├── OverlayParsingTests.cs
│   │   └── PathHelperTests.cs
│   └── Kiss/              # KISS modem tests
│       ├── KissFrameTests.cs
│       ├── KissModemTests.cs
│       └── KissSerializerTests.cs
├── ViewModels/            # ViewModel tests
│   ├── Pages/             # Page ViewModel tests
│   │   ├── AddEditPortViewModelTests.cs
│   │   ├── ConversationViewModelTests.cs
│   │   ├── DynamicBeaconingViewModelTests.cs
│   │   ├── HomeViewModelTests.cs
│   │   ├── MessagesViewModelTests.cs
│   │   ├── PacketsViewModelTests.cs
│   │   ├── PortsViewModelTests.cs
│   │   ├── SettingsViewModelPersistenceTests.cs
│   │   └── SettingsViewModelTests.cs
│   ├── Components/        # Component ViewModel tests
│   │   ├── LocationTrackingViewModelTests.cs  # ORPHANED - ViewModel deleted
│   │   ├── MemoryLeakTests.cs  # NEEDS UPDATE - references deleted ViewModels
│   │   └── PortItemViewModelTests.cs
│   └── MainViewModelTests.cs  # NEEDS UPDATE - references deleted ViewModels
├── Transports/            # Transport tests
│   └── Kiss/
│       ├── KissStreamFactoryTests.cs
│       ├── TcpKissStreamConnectorTests.cs
│       └── UnsupportedBluetoothKissStreamConnectorTests.cs
├── Data/                  # Data layer tests
│   ├── AppSavedDataInitializerTests.cs
│   ├── PortRecordMapperTests.cs
│   └── SavedDataPersistenceTests.cs
├── Extensions/            # Extension method tests
│   └── PortConfigExtensionsTests.cs
├── Helpers/               # Test helpers
│   ├── AprsPasscodeTests.cs
│   └── TempAppDatabase.cs
├── Imaging/               # Imaging tests
│   ├── AprsSymbolBitmapProviderTests.cs
│   └── AprsSymbolMapConverterTests.cs
├── Models/                # Domain model tests
│   ├── CallsignTests.cs
│   ├── PacketTests.cs
│   └── SymbolTests.cs
└── TestFixtureBase.cs     # Base test class
```

**Namespace convention:**
- Test namespace mirrors source: `AetherAprs.Tests.{SourceSubfolder}`
- Example: `AetherAprs/Modems/Kiss/KissSerializer.cs` → `AetherAprs.Tests/Modems/Kiss/KissSerializerTests.cs`
- Services: `AetherAprs.Tests.Services.{Category}`
- ViewModels: `AetherAprs.Tests.ViewModels.{Pages|Components}`

## File Placement Guidelines

### When adding new files, follow these rules:

**Configuration:**
- Application settings classes → `Configuration/Settings/`
- Port/transport configs → `Configuration/`
- UI configuration enums → `Configuration/`

**Data:**
- EF Core entities → `Data/Entities/`
- Domain-to-entity mappers → `Data/Mappers/`
- EF Core value converters → `Data/Converters/`
- Migrations → `Data/Migrations/`

**Domain Models:**
- APRS packet types → `Models/Aprs/Packets/`
- APRS value types/enums → `Models/Aprs/`
- Messaging models → `Models/Messaging/`
- General domain models → `Models/`

**Services:**
- Determine service category (Beaconing, Location, Messaging, Packets, Ports, Transmission, Platform, UI, etc.)
- Place interface and implementation in appropriate `Services/{Category}/`
- Shared DTOs → `Services/Contracts/`
- Location tracking → `Services/Location/`
- Beacon transmission coordination → `Services/Transmission/`
- Map layers, navigation, keyboard, culture → `Services/UI/`
- Platform abstractions (permissions, foreground, location hardware, app data) → `Services/Platform/`

**Converters:**
- APRS protocol converters → `Converters/Aprs/`
- UI converters → `Converters/UI/`

**Factories:**
- ViewModel factories → `Factories/ViewModels/`
- Other factories → `Factories/`

**ViewModels/Views:**
- Page-level → `{ViewModels|Views}/Pages/`
- Reusable components → `{ViewModels|Views}/Components/`
- Top-level or shared → `{ViewModels|Views}/` root

**Android-specific:**
- Bluetooth implementation → `AetherAprs.Android/Services/Bluetooth/`
- Platform services → `AetherAprs.Android/Services/Platform/`
- Transport implementations → `AetherAprs.Android/Services/Transports/`

## Namespace Conventions

1. Namespace MUST match folder structure
2. Use dot notation for nested folders: `AetherAprs.Models.Aprs.Packets`
3. Don't skip levels: `AetherAprs/Models/Aprs/Packets/` → `AetherAprs.Models.Aprs.Packets`
4. Android project: `AetherAprs.Android.Services.{Category}`
5. Test project: `AetherAprs.Tests.{SourceSubfolder}`

## Common Patterns

**Service organization:**
- Group related services by domain responsibility
- Keep interfaces and implementations together
- Shared DTOs go in `Services/Contracts/`

**Factory pattern:**
- Factories for transient ViewModels requiring initialization
- Interface + implementation in same subfolder
- Use dependency injection, never service locator

**MVVM:**
- One ViewModel per View (Page or Component)
- Register in ServiceProviderFactory, DesignData, and ViewLocator
- Child ViewModels injected via constructor, not created directly

**Data layer:**
- Clear separation: Entities (persistence) vs Models (domain)
- Mappers convert between entities and domain models
- Only services access entities directly
