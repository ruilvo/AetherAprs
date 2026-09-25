<!--
This file is part of AetherAprs
SPDX-FileCopyrightText: 2026 Rui Oliveira <ruimail24@gmail.com>
SPDX-License-Identifier: CC-BY-SA-4.0
-->
# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

## AetherAprs-Specific Verification

### After Every Code Change

```powershell
# Always build to verify compilation
dotnet build AetherAprs.slnx
```

If the build fails, fix errors before presenting the result.

### After Test-Related Changes

```powershell
# Run tests to verify functionality
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
```

If tests fail, diagnose and fix before presenting the result.

### After Creating New Files

```powershell
# Verify REUSE compliance
pre-commit run reuse-lint-file --all-files
```

Every new file MUST have an SPDX license header. Pre-commit hook will reject files without proper headers. See `AGENTS.md` for header formats.

### Platform Boundaries

- Core functionality goes in `AetherAprs/` (net10.0)
- Android-specific code goes in `AetherAprs.Android/` (net10.0-android)
- Never add Android APIs or platform-specific code to the core project
- Platform services use the override pattern: interface in core, platform implementation in Android

### Package Management

When adding packages:
1. Add `<PackageVersion Include="Name" Version="x.y.z" />` to `Directory.Packages.props`
2. Add `<PackageReference Include="Name" />` (no version) to `.csproj`
3. Build to verify

### Test Framework

Tests use **xUnit v3** (`xunit.v3`, not `xunit` v2). Run them with:

```powershell
dotnet test AetherAprs.Tests/AetherAprs.Tests.csproj
```

Key differences from xUnit v2:
- `[Fact]` is in the `Xunit` namespace
- `ValueTask`-returning async methods work directly with `Assert.ThrowsAsync<T>` (no `.AsTask()` needed)
- `IAsyncDisposable` types require `await using` in tests
- Use `TestContext.Current.CancellationToken` for test cancellation (suppresses xUnit1051 warnings)
- Test classes don't need `public`

No mocking library is currently referenced. Add one via `Directory.Packages.props` and the `.csproj` when needed.

### ImplicitUsings Disabled

All projects have `<ImplicitUsings>disable</ImplicitUsings>`. When writing C# code:
- Include explicit `using` statements for ALL namespaces
- This includes `System`, `System.Collections.Generic`, `System.Linq`, etc.
- Check existing files for examples of required usings

---

## 5. Memory Leak Prevention

**Always clean up event subscriptions. Always.**

Event handlers create strong references that prevent garbage collection. Unsubscribe in disposal or when the source changes.

Common leak patterns:

```csharp
// WRONG - Leak: lambda captures 'this', subscription never removed
public MyView()
{
    someObject.SomeEvent += (s, e) => { UseThis(); };
}

// CORRECT - Track source and unsubscribe
private SomeType? _currentSource;

private void OnSourceChanged(SomeType? newSource)
{
    if (_currentSource != null)
    {
        _currentSource.SomeEvent -= OnSomeEvent;
    }
    
    _currentSource = newSource;
    
    if (_currentSource != null)
    {
        _currentSource.SomeEvent += OnSomeEvent;
    }
}

private void OnSomeEvent(object? sender, EventArgs e)
{
    // Handle event
}
```

**Never subscribe to your own PropertyChanged with lambdas:**

```csharp
// WRONG - Self-reference leak
PropertyChanged += (s, e) => 
{
    if (e.PropertyName == nameof(Foo)) DoSomething();
};

// CORRECT - Use CommunityToolkit.Mvvm partial methods
[ObservableProperty]
public partial int Foo { get; set; }

partial void OnFooChanged(int value)
{
    DoSomething();
}
```

**Cancel background work during disposal:**

```csharp
// WRONG - Task runs after disposal
_ = Task.Run(async () => await LongRunningWorkAsync());

// CORRECT - Pass cancellation token
private readonly CancellationTokenSource _disposalCts = new();

private void StartWork()
{
    _ = Task.Run(async () => 
    {
        try
        {
            await LongRunningWorkAsync(_disposalCts.Token);
        }
        catch (OperationCanceledException) { }
    }, _disposalCts.Token);
}

public void Dispose()
{
    _disposalCts.Cancel();
    _disposalCts.Dispose();
}
```

**Async void only for event handlers, and always wrap in try-catch:**

```csharp
// ACCEPTABLE - Event handler with error handling
private async void OnButtonClick(object? sender, EventArgs e)
{
    try
    {
        await DoWorkAsync();
    }
    catch (Exception ex)
    {
        // Log - exceptions in async void crash the app
        Logger.LogError(ex, "Error in handler");
    }
}

// BETTER - Use RelayCommand for user actions
[RelayCommand]
private async Task DoWorkAsync()
{
    // Framework captures exceptions
}
```

**Thread-safe disposal:**

```csharp
// WRONG - Race condition if called from different thread
_resource?.Dispose();
_resource = null;

// CORRECT - Capture then null
var resource = _resource;
_resource = null;
resource?.Dispose();
```

---

## 6. Dependency Injection Patterns

**Never use service locator. Use constructor injection or factory pattern.**

Service locator anti-pattern:
```csharp
// WRONG - Service locator
var vm = App.GetService<AddEditPortViewModel>();
vm.Initialize(callsign, portNumber, config);
navigationService.NavigateTo(vm);
```

Factory pattern (for transient ViewModels requiring initialization):
```csharp
// CORRECT - Factory pattern
public interface IAddEditPortViewModelFactory
{
    AddEditPortViewModel CreateForAdd(Callsign callsign, int portNumber);
    AddEditPortViewModel CreateForEdit(Callsign callsign, int portNumber, PortConfig config);
}

public class AddEditPortViewModelFactory : IAddEditPortViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;
    
    public AddEditPortViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public AddEditPortViewModel CreateForAdd(Callsign callsign, int portNumber)
    {
        var vm = _serviceProvider.GetRequiredService<AddEditPortViewModel>();
        vm.Initialize(callsign, portNumber, existingConfig: null);
        return vm;
    }
    
    // ... CreateForEdit implementation
}

// Usage in consumer
private readonly IAddEditPortViewModelFactory _factory;

public PortsViewModel(IAddEditPortViewModelFactory factory, ...)
{
    _factory = factory;
}

private void OnEditPort(PortItemViewModel item)
{
    var vm = _factory.CreateForEdit(callsign, portNumber, config);
    _navigationService.NavigateTo(vm);
}
```

**Child ViewModel injection:**
```csharp
// CORRECT - Inject child ViewModels
public SettingsViewModel(AprsSymbolPickerViewModel symbolPicker, ...)
{
    SymbolPicker = symbolPicker;
    SymbolPicker.TableCharacter = config.Symbol.Table.ToChar().ToString();
}

// WRONG - Create child ViewModels directly
public SettingsViewModel(IAprsSymbolBitmapProvider provider, ...)
{
    SymbolPicker = new AprsSymbolPickerViewModel(provider); // Anti-pattern
}
```

When to use each pattern:
- **Constructor injection**: For all regular dependencies and child ViewModels
- **Factory pattern**: For transient ViewModels that need `Initialize(contextData)` called after construction
- **Service locator**: Never (except in code-behind static methods where DI unavailable)

---

## 7. Async/Await Error Handling

**Fire-and-forget is only acceptable with proper error handling and state recovery.**

Property change handlers calling async methods:
```csharp
// Pattern: Async callback with error recovery
private readonly Func<PortItemViewModel, Task> _onToggle;

partial void OnIsEnabledChanged(bool value)
{
    if (!_isInitializing)
        _ = HandleToggleAsync();
}

private async Task HandleToggleAsync()
{
    try
    {
        await _onToggle(this);
    }
    catch (Exception ex)
    {
        _logger?.LogError(ex, "Error toggling port {PortName} (ID: {PortId})", Name, Id);
        // Revert UI state on failure
        _isInitializing = true;
        IsEnabled = !IsEnabled;
        _isInitializing = false;
    }
}
```

The parent provides async implementation:
```csharp
private async Task OnTogglePortAsync(PortItemViewModel item)
{
    await _portService.SetPortEnabledAsync(item.Id, item.IsEnabled);
}
```

Key principles:
- Wrap fire-and-forget in try-catch with error logging
- Revert UI state on failure to maintain consistency
- Use structured logging with context (`ILogger<T>`, not `Debug.WriteLine`)
- Pass exceptions that user should see to UI layer (snackbar, dialog)

---

## 8. Logging Consistency

**Always use `ILogger<T>` for error logging. Never use `Debug.WriteLine` or `Console.WriteLine`.**

```csharp
// CORRECT - Structured logging
_logger?.LogError(ex, "Failed to {Operation} for port {PortName}", operation, portName);

// WRONG - Unstructured, lost in production
System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
```

Acceptable exceptions:
- `App.axaml.cs` startup failures (before logging initialized)
- Temporary debugging during development (must be removed before commit)

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.
