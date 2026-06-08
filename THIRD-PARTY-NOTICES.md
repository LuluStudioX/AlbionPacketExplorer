# Third-Party Notices

AlbionPacketExplorer builds on the third-party components listed below. Each is distributed under
its own license; this notice is retained as attribution.

## Removed: AlbionOnline-StatisticsAnalysis (GPL-3.0)

Earlier builds embedded copied decode/handler code from AlbionOnline-StatisticsAnalysis under
`libs/Abstractions`, `libs/Protocol18`, `libs/PhotonPackageParser`, and `libs/Network`, which were
**GNU General Public License v3.0 (GPL-3.0)**. Those components have been **removed**: the Photon
wire decoding is now provided by `libs/PhotonWire`, an independent in-house implementation written
from the wire format and verified for behavioural parity. No GPL-3.0 code remains in the project.

## NuGet packages

Referenced via NuGet and distributed under their respective permissive open-source licenses (MIT /
BSD); refer to each package's metadata for the exact terms:

- Avalonia, Avalonia.Desktop, Avalonia.Controls.DataGrid, Avalonia.Themes.Fluent
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Velopack
- SharpPcap (and its dependency PacketDotNet)

## Lucide icons

Icon geometries in `src/AlbionPacketExplorer/Themes/Icons.axaml` are from Lucide
(https://lucide.dev), under the **MIT License**.
