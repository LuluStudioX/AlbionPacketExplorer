# AlbionPacketExplorer

A cross-platform desktop tool for decoding and exploring Albion Online network packets.

It captures Albion's Photon traffic **live off the wire** (or loads a previously saved capture)
and decodes every raw event/request/response so you can inspect every field of every packet. The
goal is to understand packet structure well enough to write more complete event constructors for
analysis tools that consume this traffic.

Captures are stored as newline-delimited JSON. That format began as an ad-hoc debug dump while
investigating island-management events, which is what grew into this project; it is the app's own
format.

> Status: pre-1.0, under active development. Windows / Linux / macOS desktop.

---

## Download

Grab the latest build from the [Releases](https://github.com/LuluStudioX/AlbionPacketExplorer/releases) page:

| Platform | Installer | Portable |
|----------|-----------|----------|
| Windows | `...-win-x64-Setup.exe` | `...-win-x64-Portable.zip` |
| Linux | `...-linux-x64.AppImage` | — |
| macOS (Apple Silicon) | `...-osx-arm64-Setup.pkg` | `...-osx-arm64-Portable.zip` |
| macOS (Intel) | `...-osx-x64-Setup.pkg` | `...-osx-x64-Portable.zip` |

Install via the Setup/AppImage to get **auto-update** (Velopack). The other assets
(`*-full.nupkg`, `RELEASES-*`, `*.json`) are the per-platform update feed and are not meant to
be downloaded directly. Builds are currently unsigned, so the OS may warn on first launch.

---

## What it does

- **Code Aggregator** — counts every event/op code seen and which byte keys each one carries,
  so you can spot structure at a glance.
- **Packet List** — every decoded packet with a fast filter query. Scope tokens:
  `kind:` `code:` `name:` `params:`, plus exchange tokens `paired:yes|no`,
  `failed:yes|no` (responses with non-zero / zero `ReturnCode`), and `returncode:N`. Prefix any
  token with `-` to exclude, e.g. `kind:RESPONSE failed:yes -code:2`.
- **Packet Detail** — per-packet param grid with:
  - a curated schema (param names, notes, `resolveAs` tags) shipped in
    `packet-schema.base.json`, editable per-param at runtime;
  - item-index / unique-name resolution to readable item names, with icons;
  - a **Response status** banner showing Photon `ReturnCode` + `DebugMessage` for responses;
  - **request/response correlation** — paired packets link to each other and a **Diff** tab
    shows request-vs-response fields side by side (server-added fields highlighted).
- **Diff any two packets** — multi-select two rows and "Diff Selected" to compare them field
  by field (useful for two captures of the same event code: which fields are optional).
- **Live Capture** — capture packets directly off the wire (requires Npcap on Windows or
  libpcap on Linux/macOS).
- **Export** — copy a packet as JSON, save a session, or generate constructor scaffolding.
- **Auto-update** via Velopack.

---

## The capture format

Captures are newline-delimited JSON, one packet per line (written by live capture, re-loadable
later):

```json
{"ts":"2026-05-29T05:36:23Z","kind":"EVENT","code":32,"params":{"0":{"type":"Int64","value":462},"1":{"type":"Int16","value":1050},"252":{"type":"Int16","value":32}}}
```

- `kind` — `EVENT` / `REQUEST` / `RESPONSE`
- `code` — the Photon event/op code (equals the `EventCodes` / `OperationCodes` enum ordinal)
- `params` — byte-indexed payload; key `252` (events) / `253` (requests, responses) is the
  code echo, not payload

The "Open capture" picker defaults to the app's own saved-captures folder; any capture saved by
this app can be reloaded from anywhere.

---

## Build

Requires the .NET 10 SDK.

```bash
dotnet build AlbionPacketExplorer.slnx
```

Run the desktop app:

```bash
dotnet run --project src/AlbionPacketExplorer/AlbionPacketExplorer.csproj
```

The codec libraries under `libs/` (`Abstractions`, `Protocol18`, `PhotonPackageParser`,
`Network`) are third-party GPL-3.0 components; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## Schema

`tools/generate-schema.py` regenerates `packet-schema.base.json` from the `EventCodes` /
`OperationCodes` enums in a reference C# source repo, carrying curated param annotations forward by
name. Re-run it after the reference updates, since codes shift when enum members are inserted:

```bash
python tools/generate-schema.py --source-path /path/to/reference-source
```

---

## Disclaimer

Provided "as is", without warranty; the authors are not liable for misuse. Use only on traffic you
are authorised to capture, and comply with applicable laws and any service's terms. Not affiliated
with Sandbox Interactive / Albion Online. See [DISCLAIMER.md](DISCLAIMER.md).

## License

Not yet finalized. AlbionPacketExplorer currently includes third-party **GPL-3.0** components (the
`libs/` decode layer; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). Because GPL-3.0 is
copyleft, any distributed build that includes them is governed by GPL-3.0. A final `LICENSE` will
be set once those components are either kept under GPL-3.0 or replaced by an independent
implementation.
