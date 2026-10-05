// This file is part of AetherAprs
// SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
// SPDX-License-Identifier: GPL-3.0-or-later

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Xaml.Interactivity;
using System;

namespace AetherAprs.Behaviors;

/// <summary>
/// Behavior that automatically scrolls a ScrollViewer to bring focused controls into view.
/// Particularly useful when the keyboard appears and hides focused TextBoxes.
/// </summary>
public class ScrollToFocusedControlBehavior : Behavior<ScrollViewer>
{
    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject != null)
        {
            AssociatedObject.AddHandler(InputElement.GotFocusEvent, OnGotFocus, handledEventsToo: true);
        }
    }

    protected override void OnDetaching()
    {
        if (AssociatedObject != null)
        {
            AssociatedObject.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        }

        base.OnDetaching();
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (AssociatedObject == null || e.Source is not Control focusedControl)
        {
            return;
        }

        try
        {
            // Check if the focused control is a descendant of this ScrollViewer
            if (!IsDescendant(AssociatedObject, focusedControl))
            {
                return;
            }

            // Wait for layout to complete before scrolling
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                BringIntoView(focusedControl);
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch
        {
            // Ignore errors - scrolling is a nice-to-have feature
        }
    }

    private void BringIntoView(Control control)
    {
        if (AssociatedObject == null)
        {
            return;
        }

        // Get the bounds of the focused control relative to the ScrollViewer
        var transform = control.TransformToVisual(AssociatedObject);
        if (transform == null)
        {
            return;
        }

        var controlBounds = new Rect(control.Bounds.Size);
        var transformedBounds = controlBounds.TransformToAABB(transform.Value);

        // Get current scroll position
        var currentOffset = AssociatedObject.Offset;
        var viewportHeight = AssociatedObject.Viewport.Height;

        // Calculate if we need to scroll
        var controlTop = transformedBounds.Top;
        var controlBottom = transformedBounds.Bottom;

        // Add some padding (48 pixels) to ensure control isn't right at the edge
        const double padding = 48;

        double newOffsetY = currentOffset.Y;

        // If control is below viewport, scroll down
        if (controlBottom + padding > viewportHeight)
        {
            newOffsetY = currentOffset.Y + (controlBottom + padding - viewportHeight);
        }
        // If control is above viewport, scroll up
        else if (controlTop - padding < 0)
        {
            newOffsetY = currentOffset.Y + controlTop - padding;
        }

        // Ensure we don't scroll beyond bounds
        var maxOffset = AssociatedObject.Extent.Height - viewportHeight;
        newOffsetY = Math.Max(0, Math.Min(newOffsetY, maxOffset));

        // Only scroll if needed
        if (Math.Abs(newOffsetY - currentOffset.Y) > 1)
        {
            AssociatedObject.Offset = new Vector(currentOffset.X, newOffsetY);
        }
    }

    private static bool IsDescendant(Visual ancestor, Visual? descendant)
    {
        while (descendant != null)
        {
            if (descendant == ancestor)
            {
                return true;
            }
            descendant = descendant.GetVisualParent();
        }
        return false;
    }
}
