# Changelog

All notable user-facing changes to AlbionPacketExplorer. Format follows
[Keep a Changelog](https://keepachangelog.com/); versions follow semantic versioning and are
cut as annotated git tags on `main` (MinVer derives build versions between tags).

Per-release binaries and auto-generated notes live on the
[Releases](https://github.com/LuluStudioX/AlbionPacketExplorer/releases) page; this file is the
curated, human-organised history.

## [Unreleased]

## [0.17.2] - 2026-06-12

### Changed
- **Packet detail context menu**: restructured into a submenu hierarchy; copy actions grouped under
  a **Copy** submenu. Tooltips added to clarify what each action copies. Label tweaks for clarity.

## [0.17.1] - 2026-06-12

### Added
- **Tools window - Copy log button**: copies the full merge/verify log to the clipboard.

### Changed
- **Tools window - log text wrapping**: log panel now wraps long lines instead of clipping them.
- **Tools window - output path UX**: directory and filename are now shown on separate rows; the
  filename field edits the stem only with a fixed `.json` badge, preventing accidental extension
  deletion. Default output name is `merged_packets_YYYYMMDD_HHmmss` instead of `packets-merged`.

## [0.17.0] - 2026-06-12

Param value resolution: the Packet Detail **Resolved** column can now turn raw numbers and
domain strings into readable names, far beyond the previous item-index lookup.

### Added
- **Enum resolution** (`resolveAs: enum:<Name>`): integer params resolve to client enum member
  names. Ships ~160 enums verified against the client string table (`ActionComponentType`,
  `AttackType`, `GuildRole`, `ClusterQualities`, `Faction`, `EquipmentSlot`, `Rarity`, and more).
  Works with no item data loaded. Example: `2` -> `RepairItem`.
- **Domain-string resolution** (`resolveAs: str:<set>`): string params whose values are
  game-domain identifiers resolve to readable English, sourced from the community `ao-bin-dumps`
  data. First set `accessrights` (e.g. `owner` -> `Owner`, `noaccess` -> `No access`); string
  arrays resolve per element.
- **Localization resolution**: string params holding a localization key (a leading `@`, e.g.
  `@PARTYFINDER_JOINREQUEST_DECLINED`) automatically show the English text. No tag required.
- **51 curated box->enum mappings** pre-applied in the base schema (for example
  `Attack -> AttackType`, `GuildPlayerUpdated -> GuildRole`, `ClusterInfoUpdate -> ClusterQualities`,
  `AccessStatus -> AccessRightsContainers`, `ActionOnBuildingStart -> ActionComponentType`).
- **Param editor**: the *Resolve as* dropdown now lists every `enum:<Name>` and `str:<set>` option
  alongside `itemIndex`, so you can tag a param yourself; the choice persists in your user schema.
- **Generators**: `tools/build-resolve-enums.py` and `tools/build-domain-strings.py` rebuild the
  shipped value-sets from the client and `ao-bin-dumps`.
- First automated test project (`tests/AlbionPacketExplorer.Tests`) covering the resolvers and the
  shipped schema tags.

### Changed
- Enum resolution reads a param's raw integral value rather than its formatted display string, so
  large/timestamped integer params resolve correctly.

## [0.16.0] - 2026-06-11
- Earlier releases (capture pipeline, live capture, schema system, export, packet list, code
  aggregator). See the Releases page for per-tag notes.

[Unreleased]: https://github.com/LuluStudioX/AlbionPacketExplorer/compare/v0.17.2...HEAD
[0.17.2]: https://github.com/LuluStudioX/AlbionPacketExplorer/compare/v0.17.1...v0.17.2
[0.17.1]: https://github.com/LuluStudioX/AlbionPacketExplorer/compare/v0.17.0...v0.17.1
[0.17.0]: https://github.com/LuluStudioX/AlbionPacketExplorer/compare/v0.16.0...v0.17.0
[0.16.0]: https://github.com/LuluStudioX/AlbionPacketExplorer/releases/tag/v0.16.0
