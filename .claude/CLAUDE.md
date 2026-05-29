## Communication Style

Caveman Ultra mode. Technical accuracy exact. Fluff die.
Drop: articles, filler, pleasantries, preamble, hedging.
Fragments OK. Pattern: [thing] → [action] → [reason].
ACTIVE EVERY RESPONSE. No revert.

---

## Project Overview

**AlbionPacketExplorer** — Standalone Avalonia cross-platform desktop tool.
Reads `packet_sniffer.json` from AlbionOnline-StatisticsAnalysis (SAT app) and decodes all raw Albion Online network packets.
Goal: explore every field of every packet to write better, more complete event constructors in SAT.

**Stack:** C# / .NET 9, Avalonia 11, MVVM (CommunityToolkit.Mvvm)

**Solution:** `AlbionPacketExplorer.sln`

---

## Project Structure

```
libs/
  Abstractions/         ← IPhotonReceiver interface (copied from SAT)
  Protocol18/           ← Protocol18Deserializer + types (copied from SAT)
  PhotonPackageParser/  ← PhotonParser abstract class (copied from SAT)
  Network/              ← AlbionParser, packet types, handlers (copied from SAT)

src/AlbionPacketExplorer/
  Models/               ← PacketEntry, CodeStats, KeyStats, ParamValue
  Services/             ← PacketFileReader (streaming JSON), ConstructorExporter
  ViewModels/           ← MainViewModel, CodeAggregatorViewModel, PacketListViewModel, PacketDetailViewModel
  Views/                ← MainWindow.axaml
```

---

## Packet Sniffer JSON

Source file: `%LOCALAPPDATA%\StatisticsAnalysisTool\Instances\3168FFFA\temp\packet_sniffer.json`
Format: newline-delimited JSON, one packet per line. Up to 20MB / 100k lines.

```json
{"ts":"2026-05-29T05:36:23Z","kind":"EVENT","code":32,"params":{"0":{"type":"Int64","value":462},"1":{"type":"Int16","value":1050},"252":{"type":"Int16","value":32}}}
```

- `kind`: EVENT / REQUEST / RESPONSE
- `code`: game event/op code (short)
- `params`: keys are byte indices as strings ("0"–"255")
- Key "252" on EVENTs = code echo (not payload)
- Key "253" on REQUESTs/RESPONSEs = code echo (not payload)

---

## Key Island-Relevant Packet Codes (SAT context)

| Code | Kind | Name | Keys currently parsed |
|------|------|------|-----------------------|
| 32 | EVENT | NewLaborerItem | 0=objectId, 1=itemId, 2=qty, 4=emv, 7=durability |
| 35 | EVENT | LaborerObjectJobInfo | 0=objectId, 1=journalItemId, 2=isLootReady, 4=returnTime, 5=owner, 6=capacity |
| 30 | EVENT | LaborerObjectInfo | 0=objectId, 1=itemId, 2=tier, 4=returnTime, 5=owner, 6=lootState, 7=capacity, 8=itemIds[], 9=qtys[], 10=isAwayOnJob |
| 56 | EVENT | NewBuilding | 0=objectId, 1=firstName, 2=lastName, 3=uniqueName(optional), 6=lastHarvest, 7=plantedAt |
| 57 | EVENT | FarmableObjectInfo | 0=objectId, 1=isReady, 2=itemId, 3=growTime, 5=plantedAt |
| 45 | REQUEST | ActionOnBuildingStart | 0=buildingObjectId |
| 46 | REQUEST | ActionOnBuildingEnd | 0=buildingObjectId |

---

## Build

```powershell
cd D:\Users\bimas\Documents\github\AlbionPacketExplorer
dotnet build AlbionPacketExplorer.sln
```

---

## SAT Repo Path (for reference)

`D:\Users\bimas\Documents\github\AlbionOnline-StatisticsAnalysis\`
Libs source: `src/StatisticsAnalysisTool.{Abstractions,Protocol18,PhotonPackageParser,Network}/`

---

## Git Conventions

### Format

Conventional Commits 1.0.

```
<type>(<scope>): <description>
```

Subject max 72 chars. Lowercase type + scope. Imperative mood ("add" not "added").
No trailing period. No emoji. No Co-Authored-By lines.
Body: wrap at 100 chars, blank line after subject, only when "why" non-obvious.
Breaking change: append `!` to type/scope + `BREAKING CHANGE:` footer.

---

### Types

**Standard:**
| type | use |
|---|---|
| `feat` | new user-visible capability |
| `fix` | correct wrong behavior |
| `refactor` | restructure, no behavior change |
| `perf` | measurable performance improvement |
| `test` | tests only |
| `docs` | comments, XML docs, documentation |
| `style` | formatting/whitespace only |
| `chore` | repo housekeeping |
| `build` | .csproj, .slnx, build config |
| `deps` | NuGet add/remove/bump |
| `ci` | GitHub Actions, CI config |
| `revert` | reverts prior commit |

**Domain-specific:**
| type | use |
|---|---|
| `packet` | packet decoding, game code mappings, param interpretation, Models layer |
| `proto` | Protocol18Deserializer, Protocol18Stream, PhotonParser framing |
| `ui` | .axaml views, controls, visual behavior, UI-contract ViewModels |
| `export` | ConstructorExporter, C# codegen, clipboard integration |
| `capture` | LiveCaptureProvider, CaptureSession, NetworkDeviceScanner, SharpPcap |

---

### Scopes

| scope | maps to |
|---|---|
| `abstractions` | libs/Abstractions/ |
| `proto18` | libs/Protocol18/ |
| `photon` | libs/PhotonPackageParser/ |
| `network` | libs/Network/ |
| `models` | src/.../Models/ |
| `services` | src/.../Services/ |
| `viewmodels` | src/.../ViewModels/ |
| `views` | src/.../Views/ |
| `app` | src/AlbionPacketExplorer/ root |
| `deps` | .csproj package references |
| `build` | .slnx, .csproj properties, Directory.Build.props |

Two scopes for tightly coupled pairs: `(viewmodels,views)`. Never three — split commit.

---

### Atomicity

One commit = one reason to exist.

MAY combine: new ViewModel + its View; bug fix + its test; NuGet bump + required code fixes.
MUST NOT combine: feature + unrelated bug fix; refactor + behavior change; two independent features.

---

### Build Gate

Before every commit:
```powershell
dotnet build AlbionPacketExplorer.slnx
```
Zero errors. Never commit non-building code.

---

### Staging

Stage by explicit filename — never `git add .` or `git add -A`.
Run `git diff --staged` before committing.

---

### Branches

```
feature/<slug>    — new capability
fix/<slug>        — multi-commit bug fix
refactor/<slug>   — restructure that breaks main mid-work
deps/<package>    — NuGet upgrade with code adaptation
```

Merge with `--no-ff`. Never squash. Never rebase onto main. Never force-push main.
Merge commit: `type(scope): merge <branch> into main`.

---

### Versioning

**MinVer** (`Directory.Build.props`) derives all version properties from annotated git tags automatically at build time.

- NEVER set `<Version>`, `<AssemblyVersion>`, `<FileVersion>` manually in any `.csproj`.
- To release a new version: create an annotated tag on `main`.
- Between tags: MinVer auto-increments a pre-release suffix (`0.2.1-alpha.0.3+<hash>`).
- `FileVersion` / `ProductVersion` are what Sparkle/Velopack read for auto-update checks.

### Tags

Annotated, from `main` only:
```powershell
git tag -a v0.3.0 -m "v0.3.0 — <milestone summary>"
git push origin v0.3.0
```

Milestones: v0.1.0 Code Aggregator → v0.2.0 Packet List → v0.3.0 Export → v0.4.0 Live Capture → v0.5.0 Field Diff → v1.0.0 open-source ready.
