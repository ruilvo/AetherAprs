<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->

# Scrollable Page Pattern

## Overview

When creating pages with forms or text inputs where the on-screen keyboard may hide content, use the `ScrollablePageContent` component. This provides automatic scrolling to keep focused controls visible when the keyboard appears.

## Usage

Wrap your page content with `<components:ScrollablePageContent>`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:components="using:AetherAprs.Views.Components"
             ...>
  
  <components:ScrollablePageContent>
    <!-- Your page content here -->
    <StackPanel>
      <TextBox ... />
      <TextBox ... />
    </StackPanel>
  </components:ScrollablePageContent>
  
</UserControl>
```

## What It Provides

- **ScrollViewer** with `PageContent` style
- **Auto-scroll behavior** that brings focused controls into view
- **Smart padding** (48px) to ensure controls aren't at viewport edges
- **Keyboard-aware** scrolling for mobile platforms

## Example

See `AetherAprs/Views/Pages/SettingsPage.axaml` for a complete example.

## When to Use

- Pages with text input fields (TextBox, ComboBox, etc.)
- Forms that may be taller than the viewport
- Any page where keyboard appearance might hide content

## When NOT to Use

- Pages with only read-only content
- Pages that handle scrolling in a custom way
- Pages where content always fits in viewport
