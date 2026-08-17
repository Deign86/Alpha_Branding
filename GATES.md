# Gates: Native Windows migration and repository handoff

Scope: Fork ownership, inspect and migrate Alpha_Branding to validated native WPF, add guardrails, then commit, push, and open a PR.

- [x] G1: Authenticated fork, current-checkout remotes, and tracked default branch are verified.
  EVIDENCE: `gh auth status` and `gh api user --jq .login` returned authenticated user `Deign86`; fork is `https://github.com/Deign86/Alpha_Branding`; current checkout is on `chore/native-windows-app-and-guardrails`; `origin` points to the fork, `upstream` points to `Tephdy/Alpha_Branding`, and `main` tracks `origin/main`.

- [x] G2: Original repository stack, behavior, assets, configuration, and CI are inspected and a migration plan is recorded.
  EVIDENCE: Original tracked tree contained `README.md`, `index.html`, `script.js`, `styles.css`, and four PNG assets only; `PLAN.md` records the static browser stack, image workflow, branding, absent backend/config/CI, and native WPF contract.

- [x] G3: A branded native C#/.NET 8 WPF application preserves every inferable meaningful workflow without browser-rendered UI.
  CHECK: `dotnet build Alpha_Branding.sln --configuration Release --no-restore`
  EXPECT: `Build succeeded.`
  EVIDENCE: Passed with 0 warnings and 0 errors; output project is `net8.0-windows`, `WinExe`, `UseWPF=true`; seven tests cover naming, exact 1200x1000 WebP output, alpha overlay, and ZIP scope.

- [x] G4: Repository-local agent, PR, and C#/XAML quality guardrails are present and coherent.
  EVIDENCE: `AGENTS.md`, `.editorconfig`, `.gitignore`, `.github/PULL_REQUEST_TEMPLATE.md`, and `.github/workflows/pr-quality.yml` are present; workflow includes restore, build, test, format, publish, vulnerability audit, forbidden-stack scan, and non-destructive anti-slop settings.

- [x] G5: Restore, Release build, tests, framework-dependent win-x64 publish, format verification, diff checks, and startup smoke test have actual evidence.
  EVIDENCE: `dotnet format --verify-no-changes` exit 0; runtime restore exit 0; Release build exit 0; test result 7 passed, 0 failed; publish exit 0; vulnerability audit reports no vulnerable packages; forbidden scan exit 0; `git diff --check` exit 0; published `Alpha.Branding.exe` stayed running for 5 seconds and was then closed cleanly.

- [x] G6: Repository has no Tauri, Electron, WebView2, or webview-based application UI or dependency path.
  EVIDENCE: `git grep -n -i -E 'Tauri|Electron|WebView2|webview|React|Vite|Next\\.js|TypeScript|Node\\.js'` excluding planning docs returned no matches; application UI is WPF/XAML only.

- [x] G7: Intended changes are committed on `chore/native-windows-app-and-guardrails`, pushed to the authenticated fork, and a PR is open against its default branch.
  EVIDENCE: commit `106b13a7ab1464a5ef965c955b480fc8c20eb67d` pushed successfully; branch tracks `origin/chore/native-windows-app-and-guardrails`; PR opened at `https://github.com/Deign86/Alpha_Branding/pull/1` against `main`.
