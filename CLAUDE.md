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

### No Test Framework

This repo has no test framework. Verification relies on:
- Successful compilation (`dotnet build`)
- Manual testing (if you can run the app)
- Code review and inspection

Do not promise "tests pass" when no tests exist.

### ImplicitUsings Disabled

All projects have `<ImplicitUsings>disable</ImplicitUsings>`. When writing C# code:
- Include explicit `using` statements for ALL namespaces
- This includes `System`, `System.Collections.Generic`, `System.Linq`, etc.
- Check existing files for examples of required usings

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.
