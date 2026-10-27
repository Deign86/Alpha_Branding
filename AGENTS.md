# Repository Guidelines

## Native Windows application rules

- This repository is a native Windows desktop application codebase.
- Use C#, .NET 8, WPF, and XAML for application UI.
- Do not introduce React, HTML/CSS/JavaScript UI, Electron, Tauri, WebView2, embedded browser controls, or webview-based UI without explicit written approval.
- Prefer native WPF controls, resources, templates, bindings, commands, dialogs, and Windows APIs.
- Keep the solution, project files, NuGet references, build, and publish configuration reproducible.
- Verify changes with relevant `dotnet` restore, build, test, publish, and format commands.

## Native macOS application rules

- The Mac app lives in `macos/` and is a native Swift/SwiftUI app built with Apple frameworks only (ImageIO, CoreGraphics, AVFoundation); the same no-webview rule applies.
- It reuses the branding assets in `src/Alpha.Branding/Assets`; do not copy them.
- Keep its output identical to the Windows app (1200×1000 frame, pairing rules, file naming).
- Verify changes with `macos/test.sh` and `macos/build.sh`.

## Inspect first and avoid slop

- Read relevant code, assets, tests, and nearby conventions before editing.
- Make evidence-based changes; do not invent APIs, dependencies, requirements, test results, configuration values, or environment variables.
- Preserve type information and explicit contracts; validate external file input and report failures meaningfully.
- Do not add generic documentation, redundant comments, placeholder content, TODO-only code, or unrelated refactors.
- Keep changes focused and inspect `git diff` before handoff.

## Ponytail: minimum necessary solution

1. Does this need to exist? If not, do not add it.
2. Reuse an existing implementation before writing another.
3. Prefer the .NET standard library and WPF/Windows capabilities.
4. Add a dependency only for a demonstrated need; do not add a web frontend or browser shell.
5. Prefer deletion and simplification over speculative abstractions.

Do not add unrequested factories, wrappers, configuration layers, or scaffolding for hypothetical needs. Prefer boring, readable C# and XAML.

## Anti-lazy execution and verification

- Do not stop at a plan when implementation was requested.
- When a command fails, read the output, identify the root cause, fix it when caused by the change, and rerun the justified check.
- For behavior changes, add proportionate tests; if a GUI-only behavior cannot be automated, record the exact manual limitation.
- Complete the requested implementation, validation, diff review, commit, push, and PR handoff rather than claiming partial setup.
- Report actual commands and results, including warnings, package advisories, environment limitations, and residual risks.

## Taste Skill

- Project-wide UI and visual-design work must follow `.agents/skills/design-taste-frontend/SKILL.md` and `.agents/skills/redesign-existing-projects/SKILL.md`.
- Keep the upstream skill instructions unchanged; translate their guidance into native WPF/XAML patterns for this repository.

## Interface Skills

- Cross-discipline UI quality reviews and improvements follow `.agents/skills/better-interface/SKILL.md` and its domain skills:
  - UI Polish & Motion: `.agents/skills/better-ui/SKILL.md`
  - Typography: `.agents/skills/better-typography/SKILL.md`
  - Colors & Contrast: `.agents/skills/better-colors/SKILL.md`
  - Layout & Spacing: `.agents/skills/better-layout/SKILL.md`
  - Writing & Copy: `.agents/skills/better-writing/SKILL.md`
  - Accessibility & Keyboard Navigation: `.agents/skills/better-accessibility/SKILL.md`
- Translate all web tokens, CSS measurements, and browser concepts into native C# and WPF/XAML equivalents (e.g. `Typography.NumeralAlignment="Tabular"`, `TextOptions.TextFormattingMode="Display"`, native focus rings, high-contrast states, and system brushes).

## ADHD-Friendly Output Style

- Response formatting across this repository follows `.agents/skills/i-have-adhd/SKILL.md`.
- Lead with the answer or next action: command, path, or snippet first.
- Number multi-step work; one bounded action per step.
- End with one concrete next action doable in under two minutes.
- Finish the current issue before raising a new one (suppress tangents).
- Restate progress each turn ("step X of Y done").
- Give time estimates in concrete units, never "a bit".
- After a change, show what now works.
- Errors: state location, cause, and fix. No drama.
- Cap lists to 5 items.
- No preamble, no recaps, no closing pleasantries.

