# ROADMAP.md

Roadmap for **InputFlow**.

InputFlow is intended to become a polished Windows utility for bilingual and multilingual power users, not a one-off English/Korean helper. The current tested workflow is the seed case, but product decisions should generalize to people who use several Windows language profiles, IMEs, keyboard layouts, and per-app habits.

This roadmap is ordered to avoid waste: build stable product semantics first, then UI and release polish on top of those semantics. Stage gates, version lines, and GitHub Release requirements are tracked in [RELEASE_PLAN.md](RELEASE_PLAN.md).

## Product Target

InputFlow should let a user define exactly how Windows input profiles move between languages, layouts, and IME modes.

A complete product should support:

1. Two-language toggles, such as Dutch US-International <-> Korean Hangul.
2. Three-or-more-profile cycles, such as English -> Japanese -> Korean -> Chinese.
3. Direct switch hotkeys for specific profiles.
4. Optional hold-to-switch and return-to-previous workflows.
5. Reliable matching of the actual Windows input profile the user picked.
6. Safe IME mode entry, such as Korean Hangul/native mode without blind toggle keys.
7. Per-app rules and exclusions for tools, games, terminals, remote desktop, and VMs.
8. A settings UI so normal use does not require hand-editing JSON.
9. Installer, release packaging, update story, diagnostics, and recovery paths.

## Design Principles

- Do not hardcode the original English/Korean workflow into the product model.
- Prefer exact Windows-reported profile identity over loose language-name matching.
- Treat IME mode changes as state-setting operations where possible, not blind toggles.
- Do not ship speculative IME workarounds when Windows reports success but a specific app field does not refresh composition state; document the limitation and continue only when a reliable API path is found.
- Make advanced workflows possible without making the common two-profile toggle noisy.
- Keep the tray app lightweight and Windows-native.
- Avoid telemetry, ads, accounts, cloud sync, and forced admin mode by default.
- Validate configuration before applying it, and preserve a working last-known-good state.

## Current Baseline

Status: working technical preview.

Already achieved:

1. C#/.NET 8 Windows tray app.
2. Config-driven profile definitions and hotkeys.
3. Versioned config schema with v1 hotkey migration to v2 workflows.
4. Tested Dutch (Netherlands) / US-International <-> Korean Microsoft IME workflow.
5. Korean target enters Hangul/native mode through setter-style IME calls.
6. Exact installed-profile matching using Windows-reported HKL/KLID data where available.
7. Stable per-user config and log paths with legacy config migration.
8. Tray actions for opening config/log, copying diagnostics, reload, pause/resume, and exit.
9. Debounced config reloads and better startup/profile diagnostics.
10. Optional single-key trigger support, including a tested RightAlt workflow.
11. Single-instance guard for the tray app.
12. GitHub Actions build, x64 publish artifact, and tag-driven release ZIP workflow.
13. Real-world Edge address bar testing showed that browser chrome can fail to emit Hangul even when TSF language switching, HKL verification, and IMM Hangul/native state all report success; this is now treated as a documented app-field limitation, not a release-blocking core switching failure.
14. Fresh installs generate an inventory-backed starter config from installed Windows input profiles instead of assuming the original Dutch/Korean setup.

Still missing for a finished product:

1. Settings UI.
2. Guided first-run setup UI.
3. First-class multi-profile workflows beyond toggle, direct switch, and basic cycle.
4. Profile discovery/picker with exact Windows identity.
5. Installer and update UX.
6. Broader validation and automated tests.
7. Branding, icon, accessibility, and public-facing documentation polish.

## Phase 1: Config Engine V2 And Tests

Goal: define the durable behavior model that UI, samples, and releases can rely on.

Scope:

1. Introduce an explicit versioned config schema.
2. Add migrations from current v1 config.
3. Add last-known-good config handling.
4. Validate config before applying it.
5. Report validation errors clearly in logs and diagnostics.
6. Model workflows explicitly:
   - `toggle`
   - `cycle`
   - `switchTo`
   - `hold`
   - `previous`
7. Support multiple named profile groups.
8. Support multiple triggers per workflow.
9. Support per-app rules and exclusions in the schema.
10. Add focused unit tests for config parsing, migration, validation, hotkey parsing, and workflow state transitions.

Definition of done:

- Existing sample config still works or migrates cleanly.
- Invalid config does not destroy or replace a working runtime state.
- Workflow behavior is testable without Win32 calls.
- README and sample configs describe the new schema.

## Phase 2: Profile Identity And Discovery

Goal: make InputFlow reliable for people with many installed layouts and IMEs.

Scope:

1. Build a profile inventory model with language tag, layout name, profile name, HKL, KLID, LANGID, and IME metadata where available.
2. Improve logs so each configured profile explains why it matched or failed.
3. Add a diagnostics export/copy path that includes installed profile inventory and app state.
4. Research and implement TSF/InputMethodTip-backed identity where needed, focused on exact profile discovery and matching before using TSF for mode changes.
5. Preserve existing KLID/HKL behavior that fixed Dutch US-International return switching.
6. Add diagnostics that distinguish "Windows reports requested profile/mode active" from "target app field still does not accept IME composition."
7. Add tests around profile matching and ambiguous matches.

Definition of done:

- Users can distinguish similarly named layouts such as English US and English Netherlands US-International.
- Ambiguous config produces actionable errors instead of picking a surprising profile silently.
- Korean Hangul/native mode remains setter-style by default.
- Known app-specific fields, such as browser address bars, are documented honestly when they cannot be forced through supported profile/mode APIs.

## Phase 3: Settings UI And First-Run Setup

Goal: make InputFlow usable without editing JSON.

Scope:

1. Add a native settings window.
2. Add first-run setup for selecting profiles and a primary workflow.
3. Add installed-profile picker based on the Phase 2 inventory model.
4. Add trigger/hotkey picker, including single-key trigger warnings.
5. Add workflow builder for toggle, cycle, direct switch, hold, and previous workflows.
6. Add per-app rules and exclusions editor.
7. Add startup toggle.
8. Add validation-before-save with clear error display.
9. Add reset/recover options for broken config.
10. Keep manual JSON viable for power users.

Definition of done:

- A new user can configure the original Dutch/Korean workflow from the UI.
- A power user can configure at least three profiles and choose cycle/direct-switch behavior.
- The UI cannot save a config that the runtime refuses to load.

## Phase 4: Multilingual Workflow Depth

Goal: cover the workflows bilinguals and linguistic power users actually use.

Scope:

1. Multiple independent workflows.
2. Profile groups, such as writing, coding, gaming, and remote work.
3. Direct profile switch commands.
4. N-profile cycle commands.
5. Hold-to-temporary-profile commands.
6. Previous-profile and previous-non-target behavior.
7. Optional per-app or per-window remembered profile behavior.
8. Optional status overlay or toast feedback.
9. Better elevated-app detection and messaging.

Definition of done:

- InputFlow is no longer shaped around one target profile plus one fallback.
- The common two-profile toggle remains simple.
- Complex workflows remain inspectable in UI and diagnostics.

## Phase 5: Release, Installer, And Updates

Goal: make InputFlow easy to install, update, and trust.

Scope:

1. Real app icon and version metadata.
2. Portable zip release from GitHub Actions.
3. Installer, likely MSIX or another Windows-appropriate package after requirements are clear.
4. Optional startup registration from settings.
5. GitHub Releases workflow.
6. Update check or auto-update flow with transparent user control.
7. Code signing plan if feasible.
8. Upgrade tests for config migrations.

Definition of done:

- A user can install or run portable without building from source.
- Updating preserves config and does not silently change workflow behavior.
- Release notes clearly call out breaking changes and migration behavior.

## Phase 6: Polish, Accessibility, And Public Readiness

Goal: make the app feel finished.

Scope:

1. Accessibility review for settings UI and tray behavior.
2. Keyboard-only settings operation.
3. Clear docs for common workflows and known Windows limitations.
4. Troubleshooting guide built from real diagnostics cases.
5. Better samples for Korean, Japanese, Chinese, European layouts, and mixed IME/layout setups.
6. Screenshots or short setup guide.
7. Issue templates for diagnostics-heavy bug reports.

Definition of done:

- A technically comfortable user can install, configure, diagnose, and report issues without private guidance.
- Docs explain Windows limitations without hiding rough edges.

## Non-Goals By Default

Do not add these unless explicitly approved:

- Telemetry or analytics.
- Ads.
- Accounts.
- Cloud sync.
- Monetization hooks.
- Forced admin mode.
- Unsafe default Hangul toggle behavior.
- Heavy frameworks that are not justified by the app's actual UI needs.
