# Contributing to AlbionPacketExplorer

## Prerequisites

- .NET 10 SDK
- Npcap (Windows) or libpcap (Linux/macOS) for live capture features

## Build

```bash
dotnet build AlbionPacketExplorer.slnx
```

Zero build errors required before every commit. No exceptions.

## Branches

```
feature/<slug>    - new capability
fix/<slug>        - bug fix
refactor/<slug>   - restructure, no behavior change
deps/<package>    - NuGet upgrade + code adaptation
```

Branch from `main`. Merge with `--no-ff`. Never squash. Never rebase onto main.

## Commit format

[Conventional Commits 1.0](https://www.conventionalcommits.org/):

```
<type>(<scope>): <description>
```

- Subject max 72 chars, lowercase type + scope, imperative mood
- No trailing period, no emoji
- Body only when "why" is non-obvious; wrap at 100 chars

Common types: `feat` `fix` `refactor` `perf` `docs` `ui` `packet` `export` `capture`

## Cross-platform rules

Code must run unchanged on Windows, Linux, and macOS:

- Always `Path.Combine()`, never string-concatenate with `\` or `/`
- Always `Encoding.UTF8` explicit, never rely on system default
- No `Registry`, `WinForms`, `Win32` P/Invoke, `Microsoft.Win32`, or `System.Drawing`
- Platform branches only via `OperatingSystem.IsWindows()` / `IsLinux()` / `IsMacOS()`

## Pull requests

1. Branch off `main`
2. Build gate: `dotnet build AlbionPacketExplorer.slnx` - zero errors
3. Open a PR against `main` using the PR template
4. All CI checks must pass

## Reporting issues

Use the [issue templates](.github/ISSUE_TEMPLATE/) - bug report or feature request.
Include OS, .NET SDK version, and steps to reproduce for bugs.

## License

By contributing you agree your contributions are licensed under the [MIT License](LICENSE).
