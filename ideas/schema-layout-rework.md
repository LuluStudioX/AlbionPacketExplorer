# Schema System + Layout Rework

Status: PLANNED
Priority: High

---

## Problem Statement

1. Packet Detail shows raw key numbers (0, 1, 2...) — no semantic meaning
2. Unknown events have zero documentation — need a way to build knowledge incrementally
3. Current 3-panel layout (summary top, list+detail bottom split) gets cramped with many params
4. No way to export discovered knowledge back to the community / consuming tools

---

## Part 1 — Packet Schema System

### Data Model

Two JSON files, same schema:

```json
{
  "EVENT:32": {
    "name": "NewSimpleItem",
    "params": {
      "0": { "name": "objectId",  "note": "" },
      "1": { "name": "itemIndex", "note": "Int16 item ID — resolve via IndexedItems.json" },
      "2": { "name": "quantity",  "note": "" },
      "4": { "name": "emv",       "note": "Estimated market value in silver" },
      "7": { "name": "durability","note": "" }
    }
  },
  "REQUEST:45": {
    "name": "ActionOnBuildingStart",
    "params": {
      "0": { "name": "ticks",           "note": "" },
      "1": { "name": "buildingObjectId","note": "" },
      "2": { "name": "actionType",      "note": "Enum — values TBD" }
    }
  }
}
```

Key format: `"{KIND}:{CODE}"` — e.g. `"EVENT:32"`, `"REQUEST:45"`, `"RESPONSE:45"`.

### Files

| File | Location | Purpose |
|------|----------|---------|
| `packet-schema.base.json` | `src/.../Assets/` (embedded resource) | Auto-generated from the reference source. Read-only. Shipped with app. |
| `packet-schema.user.json` | `%LOCALAPPDATA%\AlbionPacketExplorer\` | User edits/additions. Persisted. Never overwritten by app updates. |

### Merge Rules

- User file wins per `(kind:code, paramKey)` — allows override of any base entry
- Base file provides fallback for keys user hasn't touched
- Both files loaded at startup by `PacketSchemaService`
- If user file missing → create empty `{}` on first edit

### PacketSchemaService

```csharp
public class PacketSchemaService
{
    // Load base (embedded) + user (disk), merge
    public Task LoadAsync();

    // Get merged schema for one packet type
    public PacketTypeSchema? GetSchema(string kind, int code);

    // Get param entry for one key
    public ParamSchema? GetParam(string kind, int code, string key);

    // Save a user edit (name or note) for one param
    public Task SaveUserParamAsync(string kind, int code, string key, string name, string note);

    // Save a user edit for the event-level name
    public Task SaveUserEventNameAsync(string kind, int code, string name);

    // Export merged schema for one event as standalone JSON
    public string ExportEventSchema(string kind, int code);
}
```

### Code Generation Tool

`tools/generate-schema.py` (or a C# console project `tools/SchemaExtractor/`) that:
1. Reads the reference source's `Network/Events/*.cs` and `Network/Handler/*.cs`
2. Extracts param key → property name mappings via Roslyn or regex
3. Outputs `packet-schema.base.json`
4. Run manually when the reference repo updates — output committed to repo

Initial base file populated from the ~30 events already extracted in research.

---

## Part 2 — PacketDetailView Schema Column

### Key Column

Change from plain `"0"` to `"0  objectId"` (schema name shown inline, greyed out).
If no schema: just `"0"`.

### Note Tooltip

Each row: if `ParamSchema.Note` non-empty → info icon (ℹ) in key column, tooltip shows note on hover.

### Inline Edit

Right-click param row → context menu:
- "Edit param name..." → opens small inline popover (TextBox pre-filled with current name)
- "Edit note..." → same for note field
- "Clear user override" → removes user entry, falls back to base schema

Edits call `PacketSchemaService.SaveUserParamAsync` immediately — no save button needed.

### ParamRow changes

```csharp
public record ParamRow(
    string Key,           // "0"
    string SchemaName,    // "objectId" (from schema, empty if unknown)
    string Type,
    string Value,
    string ResolvedName,  // item lookup
    string UniqueName,
    string Note           // from schema
);
```

---

## Part 3 — Layout Rework

### Current Layout

```
[Toolbar]
[Event Summary                    ] ← Row 0, 320px
[─────────────────────────────────] ← GridSplitter
[Packet List      | Packet Detail ] ← Row 2, remaining
```

### New Layout Option: "Focus Mode"

```
[Toolbar]
[Event Summary  (collapsed header)] ← Row 0, collapsible ~120px
[Packet List                      ] ← Row 1, fixed ~300px
[Packet Detail                    ] ← Row 2, remaining (most space)
```

Toggle: toolbar button "Focus" / "Overview" (or keyboard shortcut F5).

### Collapse Behaviour

- **Overview mode** (default): current 3-panel layout preserved exactly, no regression
- **Focus mode**: 
  - Event Summary collapses to just the header bar (40px) — click to expand
  - Packet List gets fixed height ~280px, scrollable
  - Packet Detail gets all remaining height — more rows visible
  - When packet selected: Event Summary auto-collapses, Packet Detail auto-expands
  - "Auto-collapse on select" toggle in Settings

### Implementation

- `LayoutState` gains `ViewMode` enum (`Overview`, `Focus`) and `AutoCollapse` bool
- `MainWindow.axaml`: add third `RowDefinition` for Packet Detail when in Focus mode
- `MainViewModel`: `ViewMode` observable property, drives row height bindings
- No AXAML code-behind layout switching — use `RowDefinition` height bindings driven by VM

### Single-column grid option

When Focus mode active, the bottom section becomes a single full-width column:
- `ColumnDefinitions="*"` (drop the `2*,4,*` split)
- Packet Detail fills full width — Resolved column, Value column, all visible without horizontal scroll
- GridSplitter hidden

---

## Part 4 — Schema Export

### Settings Window Addition

New "Schema" tab in SettingsWindow with:
- Dropdown: select event kind + code (filtered to events seen in current session)
- Preview: merged schema JSON for selected event
- "Copy to clipboard" button
- "Save as .json file" button

### Export Format

Standalone file, reusable annotation:

```json
{
  "source": "AlbionPacketExplorer",
  "version": "0.3.0",
  "exportedAt": "2026-05-29T...",
  "kind": "EVENT",
  "code": 32,
  "name": "NewSimpleItem",
  "params": {
    "0": { "name": "objectId",   "type": "Int64",  "note": "Building/player object ID" },
    "1": { "name": "itemIndex",  "type": "Int16",  "note": "Item ID — map via IndexedItems.json" },
    "2": { "name": "quantity",   "type": "Int32",  "note": "" },
    "4": { "name": "emv",        "type": "Int64",  "note": "Estimated market value in silver" },
    "7": { "name": "durability", "type": "Single", "note": "" }
  }
}
```

This format is directly usable for a downstream PR — the handler author can read it and implement the DTO.

---

## Implementation Order

| Step | Scope | Commit type |
|------|-------|-------------|
| 1 | Generate `packet-schema.base.json` from extracted reference data | `packet(assets)` |
| 2 | `PacketSchemaService` — load, merge, query | `packet(services)` |
| 3 | `ParamRow` gains `SchemaName` + `Note` | `packet(models)` |
| 4 | `PacketDetailViewModel` wires schema lookups | `packet(viewmodels)` |
| 5 | `PacketDetailView` — key column + note tooltip | `ui(views)` |
| 6 | Right-click edit popover | `ui(views,viewmodels)` |
| 7 | Focus mode layout | `ui(views,viewmodels)` |
| 8 | Schema export in Settings | `export(views,viewmodels)` |

Steps 1-6 are independent of 7-8. Do 1-6 first — schema is the foundation.
Step 7 (layout) can be done in parallel or after, no dependency.

---

## Decisions

- **Schema extractor**: Python — cross-platform, no build step, one-off tool. Runs with stdlib only.
- **Focus mode trigger**: toolbar button + F5 shortcut both. Shortcut configurable in Settings (future).
- **Edit popover**: Avalonia `Popup` — lighter, no taskbar entry, positioned relative to row. If Popup proves unreliable on Linux/macOS, fall back to small `Window` (both paths implemented, Settings toggle).
- **Schema export**: full merged schema always — most useful for downstream PRs.
- **Numeric item resolution via schema**: YES. Schema entry gains optional `"resolveAs": "itemIndex"` field. When set, the value is looked up in `GameDataService` by index. This replaces the broken "resolve all Int16s" approach — only schema-declared item keys get resolved. Means EVENT 32 key 1 = itemIndex → shows item name. EVENT 32 key 0 = objectId → no item lookup.
