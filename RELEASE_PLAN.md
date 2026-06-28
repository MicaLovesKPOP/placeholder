# RELEASE_PLAN.md

Release plan for **InputFlow**.

InputFlow releases are stage-gated. A stage is complete only when the code, docs, validation notes, and downloadable Windows build are ready for users at that stage. Every public stage release should be a GitHub Release created from a version tag and should include a Windows x64 portable ZIP containing `InputFlow.exe`.

## Release Principles

- Release only behavior we can explain and support.
- Do not claim broad IME mode automation until each IME mode adapter is proven safe.
- Do not block a release on unsupported app-specific chrome fields when Windows reports the requested input profile and mode as active; document those limitations instead.
- Preserve working user configs during upgrades.
- Keep releases lightweight: portable ZIP first, installer later.
- Every release must include clear limitations and manual Windows test results.
- No telemetry, ads, accounts, cloud sync, or forced admin mode by default.

## Version Lines

| Version line | Stage | Audience | Release intent |
| --- | --- | --- | --- |
| `v0.1.x` | Technical preview | Project owner and early testers | Validate the core switching engine and diagnostics on real machines. |
| `v0.2.x` | First multilingual preview | Broader technical users | First release useful beyond the original Dutch/Korean setup. |
| `v0.3.x` | Settings beta | Power users | Configure workflows without hand-editing JSON. |
| `v1.0.0` | Public stable | Daily-driver users | Stable install/update/config story with conservative support claims. |
| `v1.x` | Expansion releases | Multilingual power users | Add deeper IME adapters, workflow depth, and polish without breaking configs. |

## Required Gates For Every Release

A release tag can be created only after:

1. GitHub Actions build succeeds.
2. Core tests pass.
3. Windows x64 publish succeeds.
4. Portable ZIP artifact is produced.
5. Manual smoke testing is completed on Windows for any changed runtime behavior.
6. `Copy Diagnostics` is checked when input-profile behavior changed.
7. README, roadmap, validation notes, and release notes reflect the actual state.
8. Known limitations are stated clearly.

## Stage 0: Current Technical Preview

Target version line: `v0.1.x`.

Purpose:

- Prove the switching engine on the seed workflow.
- Collect diagnostics from real Windows profile setups.
- Keep release scope narrow and explicitly technical-preview quality.

Must have:

- Tray app starts and exits cleanly.
- Config-driven workflow loading.
- Tested Dutch US-International <-> Korean Microsoft IME workflow.
- Korean target enters Hangul/native mode through setter-style IME calls.
- Release notes document that browser address bars can behave differently from normal page/application text fields.
- RightAlt single-key trigger support remains explicit and visible.
- Diagnostics copy includes installed profiles and match reports.
- GitHub Release can attach a portable Windows x64 ZIP.

Not required yet:

- First-run setup.
- Settings UI.
- Installer.
- Auto-update.
- Broad IME mode adapters beyond Korean Hangul.

## Stage 1: First Multilingual Preview

Target version line: `v0.2.x`.

Purpose:

- Make InputFlow useful to multilingual users beyond the original owner setup.
- Stop relying on hardcoded default profiles.
- Establish the product model for profiles, workflows, and fallback behavior.

Must have:

1. Stable per-user config location instead of config next to whichever EXE folder was unpacked.
2. First-run setup path or equivalent guided config creation, starting with an inventory-backed starter config and completed by a profile/workflow picker.
3. Installed-profile picker backed by diagnostics inventory.
4. Profile health states: matched, missing, ambiguous, and changed.
5. Workflows for:
   - two-profile toggle
   - N-profile cycle
   - direct switch
   - previous profile
6. Trigger picker with clear warnings for RightAlt/AltGr and other single-key triggers.
7. Regular layout switching and IME profile switching both treated as first-class profile workflows.
8. Korean Hangul remains the first safe mode adapter.
9. Manual JSON remains viable for power users.
10. README explains positioning as a multilingual input-profile workflow utility.

Not required yet:

- Full installer.
- Auto-update.
- Japanese/Chinese mode automation unless it is safely implemented and tested.
- Per-app remembered profile behavior.

## Stage 2: Settings Beta

Target version line: `v0.3.x`.

Purpose:

- Make daily configuration possible without JSON editing.
- Support common power-user workflows cleanly.

Must have:

1. Native settings window.
2. Edit existing profiles, groups, workflows, triggers, and exclusions.
3. Validate before saving.
4. Reset/recover broken config.
5. Startup toggle.
6. Multiple named profile groups, such as Writing, Coding, CJK, and Gaming.
7. Clear in-app warnings for unavailable or ambiguous profiles.
8. Keyboard-accessible settings flow.

## Stage 3: Public Stable

Target version: `v1.0.0`.

Purpose:

- Be trustworthy as a daily-driver utility.

Must have:

1. Portable ZIP and installer story.
2. Versioned config migrations with tests.
3. Real icon and version metadata.
4. Release notes and upgrade notes for every release.
5. Documented support matrix for layouts, IMEs, mode adapters, elevated apps, RDP/VMs, and games.
6. Accessibility pass for settings and tray flows.
7. Troubleshooting guide based on diagnostics.
8. Code signing plan documented, even if signing is deferred.

## Stage 4: IME And Workflow Expansion

Target version line: `v1.x`.

Purpose:

- Add deeper support after the core app is stable.

Candidate scope:

- Japanese mode adapter, only if setter-style behavior is reliable.
- Chinese mode adapter, only if setter-style behavior is reliable.
- Hold-to-temporary-profile workflows.
- Per-app and per-window remembered profile behavior.
- Optional overlay or toast feedback.
- Import/export workflow presets.

## Release Tagging

Use semantic-ish version tags:

```text
v0.1.0
v0.2.0
v0.3.0
v1.0.0
```

Patches within a stage use patch versions, for example `v0.2.1`.

The tag push should trigger the release workflow and create a GitHub Release with a portable ZIP asset named like:

```text
InputFlow-v0.2.0-win-x64.zip
```
