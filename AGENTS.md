# Repository Guidelines

## Native Windows application rules

- This repository is a native Windows desktop application codebase.
- Use C#, .NET 8, WPF, and XAML for application UI.
- Do not introduce React, HTML/CSS/JavaScript UI, Electron, Tauri, WebView2, embedded browser controls, or webview-based UI without explicit written approval.
- Prefer native WPF controls, resources, templates, bindings, commands, dialogs, and Windows APIs.
- Keep the solution, project files, NuGet references, build, and publish configuration reproducible.
- Verify changes with relevant `dotnet` restore, build, test, publish, and format commands.

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

