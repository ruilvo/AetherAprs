// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using AetherAprs.Services.UI;
using AetherAprs.ViewModels.Components;

using NSubstitute;

using Xunit;

namespace AetherAprs.Tests.ViewModels.Components;

public sealed class MapViewModelTests
{
    [Fact]
    public void ZoomIn_RaisesZoomInRequested()
    {
        MapViewModel vm = new(Substitute.For<IUserLocationLayerService>());
        int raised = 0;
        vm.ZoomInRequested += (_, _) => raised++;

        vm.ZoomInCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ZoomOut_RaisesZoomOutRequested()
    {
        MapViewModel vm = new(Substitute.For<IUserLocationLayerService>());
        int raised = 0;
        vm.ZoomOutRequested += (_, _) => raised++;

        vm.ZoomOutCommand.Execute(null);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void CenterOnUser_NoLocation_DoesNotRaise()
    {
        IUserLocationLayerService userLocation = Substitute.For<IUserLocationLayerService>();
        userLocation.CurrentMapPoint.Returns((Mapsui.MPoint?)null);
        MapViewModel vm = new(userLocation);
        int raised = 0;
        vm.CenterOnPointRequested += (_, _) => raised++;

        vm.CenterOnUserCommand.Execute(null);

        Assert.Equal(0, raised);
    }
}
