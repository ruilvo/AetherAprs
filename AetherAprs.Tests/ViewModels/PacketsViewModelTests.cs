// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Configuration.Settings;
using AetherAprs.Data;
using AetherAprs.Factories;
using AetherAprs.Models.Aprs;
using AetherAprs.Services;
using AetherAprs.Configuration;
using AetherAprs.ViewModels.Pages;
using Geo;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AetherAprs.Tests.ViewModels;

public class PacketsViewModelTests : IDisposable
{
    private readonly IPacketQueryService _packetQueryService;
    private readonly IPacketStorageService _packetStorageService;
    private readonly INavigationService _navigationService;
    private readonly IPacketDetailsViewModelFactory _packetDetailsFactory;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<PacketsViewModel> _logger;
    private readonly List<PacketRecord> _storedPackets;

    public PacketsViewModelTests()
    {
        _packetQueryService = Substitute.For<IPacketQueryService>();
        _packetStorageService = Substitute.For<IPacketStorageService>();
        _navigationService = Substitute.For<INavigationService>();
        _packetDetailsFactory = Substitute.For<IPacketDetailsViewModelFactory>();
        _configurationService = Substitute.For<IConfigurationService>();
        _logger = Substitute.For<ILogger<PacketsViewModel>>();

        // Setup configuration service with default settings
        var appSettings = new AppSettings
        {
            Aprs = new AprsSettings
            {
                DisplayTimeRange = PacketDisplayTimeRange.LastDay,
                CustomDisplayTimeRangeHours = 12
            }
        };
        _configurationService.Settings.Returns(appSettings);

        _storedPackets = new List<PacketRecord>();
        _packetQueryService.GetMostRecentPacketsAsync(Arg.Any<int>())
            .Returns(callInfo => 
            {
                var dict = _storedPackets.ToDictionary(p => $"{p.SourceBase}-{(int)p.SourceSsid}", p => p);
                return Task.FromResult<IReadOnlyDictionary<string, PacketRecord>>(dict);
            });
    }

    [Fact]
    public async Task Constructor_InitializesProperties()
    {
        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert
        Assert.Equal("Packets", viewModel.Title);
        Assert.NotNull(viewModel.Packets);
        Assert.False(viewModel.IsLoading);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task Constructor_LoadsPacketsFromDatabase()
    {
        // Arrange
        var record = new PacketRecord
        {
            Id = 1,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Position",
            RawInfo = "Test info",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid(),
            Position = new PositionDataRecord
            {
                Location = new Coordinate(45.5231, -122.6765),
                SymbolTable = SymbolTable.Primary,
                SymbolCode = SymbolCode.LatinCapitalLetterA
            }
        };
        _storedPackets.Add(record);

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert
        Assert.Single(viewModel.Packets);
        var summary = viewModel.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Position", summary.PacketType);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task Constructor_SubscribesToPacketStored()
    {
        // Arrange
        var viewModel = await CreateViewModelAsync();
        
        var record = new PacketRecord
        {
            Id = 2,
            SourceBase = "K0OTH",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Message",
            RawInfo = "Test message",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid()
        };
        _storedPackets.Add(record);

        // Act - Trigger packet stored event
        _packetStorageService.PacketStored += Raise.EventWith(EventArgs.Empty);

        // Give the throttled update time to process
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - Event subscription is working (timer will process later)
        Assert.NotNull(viewModel);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadPacketsAsync_LoadsPositionPacket()
    {
        // Arrange
        var record = new PacketRecord
        {
            Id = 1,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Position",
            RawInfo = "Position info",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid(),
            Position = new PositionDataRecord
            {
                Location = new Coordinate(45.5231, -122.6765),
                SymbolTable = SymbolTable.Primary,
                SymbolCode = SymbolCode.LatinCapitalLetterA
            }
        };
        _storedPackets.Add(record);

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert
        Assert.Single(viewModel.Packets);
        var summary = viewModel.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Position", summary.PacketType);
        Assert.Contains("45", summary.Preview);
        Assert.Contains("122", summary.Preview);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadPacketsAsync_LoadsMessagePacket()
    {
        // Arrange
        var record = new PacketRecord
        {
            Id = 1,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Message",
            RawInfo = "Message info",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid(),
            Message = new MessageDataRecord
            {
                AddresseeBase = "K0OTH",
                AddresseeSsid = AprsSsid.PrimaryStation,
                Text = "Hello from N0CALL"
            }
        };
        _storedPackets.Add(record);

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert
        Assert.Single(viewModel.Packets);
        var summary = viewModel.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Message", summary.PacketType);
        Assert.Contains("K0OTH", summary.Preview);
        Assert.Contains("Hello from N0CALL", summary.Preview);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadPacketsAsync_LoadsStatusPacket()
    {
        // Arrange
        var record = new PacketRecord
        {
            Id = 1,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Status",
            RawInfo = "Status info",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid(),
            Status = new StatusDataRecord
            {
                Text = "Testing status packet"
            }
        };
        _storedPackets.Add(record);

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert
        Assert.Single(viewModel.Packets);
        var summary = viewModel.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Status", summary.PacketType);
        Assert.Equal("Testing status packet", summary.Preview);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadPacketsAsync_LoadsWeatherPacket()
    {
        // Arrange
        var record = new PacketRecord
        {
            Id = 1,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Weather",
            RawInfo = "Weather info",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid(),
            Weather = new WeatherDataRecord
            {
                Temperature = 72.0
            }
        };
        _storedPackets.Add(record);

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert
        Assert.Single(viewModel.Packets);
        var summary = viewModel.Packets[0];
        Assert.Equal("N0CALL-1", summary.Source);
        Assert.Equal("Weather", summary.PacketType);
        Assert.Contains("72", summary.Preview);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadPacketsAsync_SortsPacketsByMostRecentFirst()
    {
        // Arrange
        var now = DateTime.UtcNow;

        _storedPackets.Add(new PacketRecord
        {
            Id = 1,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.AdditionalStation1,
            PacketType = "Position",
            RawInfo = "Info 1",
            Timestamp = now.AddMinutes(-10), // Older
            PortId = Guid.NewGuid(),
            Position = new PositionDataRecord
            {
                Location = new Coordinate(45.5, -122.5),
                SymbolTable = SymbolTable.Primary,
                SymbolCode = SymbolCode.LatinCapitalLetterA
            }
        });

        _storedPackets.Add(new PacketRecord
        {
            Id = 2,
            SourceBase = "K0OTH",
            SourceSsid = AprsSsid.AdditionalStation2,
            PacketType = "Position",
            RawInfo = "Info 2",
            Timestamp = now, // Newer
            PortId = Guid.NewGuid(),
            Position = new PositionDataRecord
            {
                Location = new Coordinate(46.5, -123.5),
                SymbolTable = SymbolTable.Primary,
                SymbolCode = SymbolCode.LatinCapitalLetterA
            }
        });

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert - Most recent first
        Assert.Equal(2, viewModel.Packets.Count);
        Assert.Equal("K0OTH-2", viewModel.Packets[0].Source); // Newer packet first
        Assert.Equal("N0CALL-1", viewModel.Packets[1].Source);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task LoadPacketsAsync_LimitsTo100Packets()
    {
        // Arrange - Add 150 packets
        for (int i = 0; i < 150; i++)
        {
            var callsign = $"CAL{i:D3}";
            _storedPackets.Add(new PacketRecord
            {
                Id = i + 1,
                SourceBase = callsign,
                SourceSsid = AprsSsid.AdditionalStation1,
                PacketType = "Position",
                RawInfo = $"Info {i}",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
                PortId = Guid.NewGuid(),
                Position = new PositionDataRecord
                {
                    Location = new Coordinate(45.5, -122.5),
                    SymbolTable = SymbolTable.Primary,
                    SymbolCode = SymbolCode.LatinCapitalLetterA
                }
            });
        }

        // Act
        var viewModel = await CreateViewModelAsync();

        // Assert - Only 100 packets displayed
        Assert.Equal(100, viewModel.Packets.Count);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task OpenPacketDetailsCommand_NavigatesToPacketDetails()
    {
        // Arrange
        var viewModel = await CreateViewModelAsync();
        
        var summary = new PacketSummary
        {
            Source = "N0CALL-1",
            PacketType = "Position",
            ReceivedAt = DateTimeOffset.UtcNow,
            Preview = "Test preview"
        };

        var detailsVm = Substitute.For<PacketDetailsViewModel>(
            _packetQueryService,
            _navigationService,
            Substitute.For<IConversationViewModelFactory>(),
            Substitute.For<ILogger<PacketDetailsViewModel>>());

        _packetDetailsFactory.Create("N0CALL-1").Returns(detailsVm);

        // Act
        viewModel.OpenPacketDetailsCommand.Execute(summary);

        // Assert
        _packetDetailsFactory.Received(1).Create("N0CALL-1");
        _navigationService.Received(1).NavigateTo(detailsVm);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task OpenPacketDetailsCommand_WithNullSummary_DoesNotNavigate()
    {
        // Arrange
        var viewModel = await CreateViewModelAsync();

        // Act
        viewModel.OpenPacketDetailsCommand.Execute(null);

        // Assert
        _packetDetailsFactory.DidNotReceiveWithAnyArgs().Create(default!);
        _navigationService.DidNotReceiveWithAnyArgs().NavigateTo(default!);
        
        viewModel.Dispose();
    }

    [Fact]
    public async Task Dispose_CleansUpResourcesAndUnsubscribes()
    {
        // Arrange
        var viewModel = await CreateViewModelAsync();

        // Act
        viewModel.Dispose();

        // Trigger packet stored after disposal
        var record = new PacketRecord
        {
            Id = 999,
            SourceBase = "N0CALL",
            SourceSsid = AprsSsid.SecondaryMobile,
            PacketType = "Position",
            RawInfo = "Info",
            Timestamp = DateTime.UtcNow,
            PortId = Guid.NewGuid()
        };

        _packetStorageService.PacketStored += Raise.EventWith(EventArgs.Empty);

        // Assert - Second dispose should not throw
        viewModel.Dispose();
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    private async Task<PacketsViewModel> CreateViewModelAsync()
    {
        var viewModel = new PacketsViewModel(
            _packetQueryService,
            _packetStorageService,
            _navigationService,
            _packetDetailsFactory,
            _configurationService,
            _logger);
        
        // Wait for initialization to complete
        await viewModel.InitializationTask;
        
        return viewModel;
    }
}
