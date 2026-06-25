# ROADMAP.md

Roadmap for **InputFlow**.

This roadmap separates near-term stabilization from public-release polish. It is not a promise that all items are in scope immediately.

## Current Milestone: Working Personal Prototype

Status: mostly achieved, pending verification of latest repo cleanup state.

Known working core workflow:

```text
English (Netherlands) / United States-International -> Korean + Hangul/native mode
Korean -> English (Netherlands) / United States-International
```

The current prototype is useful for the original workflow but not yet polished for broader public use.

## Phase 1: Stabilize The Prototype

Goal: make the current working prototype easy to run, debug, and maintain.

Priority items:

1. Verify latest build and warning state.
2. Ensure `.gitignore` excludes local runtime/build output.
3. Ensure README, sample config, and GitHub Actions are correct.
4. Improve tray menu:
   - Open config
   - Open log
   - Reload config
   - Pause/resume
   - Exit
5. Add better diagnostics:
   - log config path
   - log installed input profiles
   - log configured-profile match results
   - log foreground/elevation mismatch when detectable
6. Debounce config reloads to avoid duplicate reloads.

## Phase 2: First Public Alpha

Goal: make InputFlow safe for technically comfortable users to try.

Possible scope:

1. Clear portable release layout.
2. GitHub release zip workflow.
3. Better error messages in logs.
4. More sample configs.
5. More robust hotkey parsing and validation.
6. Detect and report hotkey registration conflicts clearly.
7. Add a real app icon.
8. Add "copy diagnostics" or "open diagnostics folder" action.
9. Document known Windows IME limitations.

## Phase 3: Settings And First-Run UX

Goal: reduce manual JSON editing.

Possible scope:

1. First-run setup wizard.
2. Installed profile picker.
3. Hotkey picker.
4. Fallback profile picker.
5. Per-app exclusion editor.
6. Startup toggle.
7. Safe config validation before saving.
8. Reset/recover config option.

## Phase 4: Broader Language Power-User Support

Goal: support more bilingual and multilingual workflows.

Possible scope:

1. Multiple target profiles.
2. Hold mode.
3. Cycle mode.
4. Per-app profile rules.
5. Return-to-last-non-target behavior with better profile identity.
6. Optional overlay/status indicator.
7. Optional sound/haptic-style feedback where appropriate.
8. Better TSF/InputMethodTip profile matching.

## Phase 5: Release Polish

Goal: make InputFlow feel like a polished Windows utility.

Possible scope:

1. Signed binary, if feasible.
2. Installer, if the user wants one.
3. Portable zip as the primary lightweight release.
4. Auto-update check, only if explicitly requested and designed transparently.
5. Better documentation site or GitHub Pages, if useful.
6. Accessibility review for tray/settings UI.

## Long-Term Ideas

These are not immediate tasks:

- Advanced TSF compartment support.
- Per-window input profile memory.
- Multi-device config sync.
- Export/import profiles.
- Community-submitted sample configs.

## Out Of Scope By Default

Unless explicitly requested, do not add:

- Telemetry
- Analytics
- Ads
- Accounts
- Cloud sync
- Monetization
- Heavy frameworks
- Forced admin mode
- Unsafe default Hangul toggle behavior
