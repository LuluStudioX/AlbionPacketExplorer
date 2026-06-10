# Manual/local fold: pulls shared digests, rebuilds BOTH overlays (captures -> inferred,
# digests -> digest-overlay), regenerates the schema, runs the build gate.
# The DAILY automated path is the GitHub Actions workflow .github/workflows/digest-sync.yml
# (digests only); this script is for local runs that also fold capture files.
#
#   .\sync-digests.ps1            # sync, leave changes uncommitted for review
#   .\sync-digests.ps1 -Commit    # sync + commit schema files when build passes
#   .\sync-digests.ps1 -Log       # also append output to sync.log
#
# Requires: wrangler auth (npx wrangler login), python, dotnet, git in PATH.

param(
    [switch] $Commit,
    [switch] $Log
)

$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$logFile = Join-Path $PSScriptRoot "sync.log"

function Say($msg) {
    Write-Host $msg
    if ($Log) { "$((Get-Date).ToString('s'))  $msg" | Out-File $logFile -Append -Encoding utf8 }
}

Say "=== digest sync start ==="

# 1. Pull digests into ./digests/ (no KV drain: entries expire on their own after 60 days,
#    and the archive dedupes by content-hash file name).
& (Join-Path $PSScriptRoot "pull-digests.ps1") | ForEach-Object { Say $_ }

# 2. Captures -> inferred-overlay.json.
$captureDirs = @(
    "$env:LOCALAPPDATA\StatisticsAnalysisTool\Instances\3168FFFA\temp",
    "$env:LOCALAPPDATA\AlbionPacketExplorerData\logs"
)
$captures = @()
foreach ($d in $captureDirs) {
    if (Test-Path $d) {
        $captures += Get-ChildItem "$d\*.json" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -notmatch '^(layout|packet-schema|inferred-overlay)' } |
            Select-Object -ExpandProperty FullName
    }
}
if ($captures.Count -gt 0) {
    $pyArgs = @($captures | ForEach-Object { $_ -replace '\\', '/' })
    python (Join-Path $repo "tools\build-inference-overlay.py") @pyArgs | ForEach-Object { Say $_ }
    if ($LASTEXITCODE -ne 0) { Say "capture overlay build FAILED"; exit 1 }
} else {
    Say "No local captures found; keeping the committed inferred-overlay.json."
}

# 3. Digest archive -> digest-overlay.json (same artifact CI produces).
$digests = Get-ChildItem (Join-Path $PSScriptRoot "digests\*.json") -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty FullName
if ($digests.Count -gt 0) {
    $pyArgs = @($digests | ForEach-Object { $_ -replace '\\', '/' })
    python (Join-Path $repo "tools\build-inference-overlay.py") --out (Join-Path $repo "tools\digest-overlay.json") @pyArgs |
        ForEach-Object { Say $_ }
    if ($LASTEXITCODE -ne 0) { Say "digest overlay build FAILED"; exit 1 }
}

# 4. Regenerate schema. Source = this repo's own enum copies (APX is standalone; no SAT).
python (Join-Path $repo "tools\generate-schema.py") | ForEach-Object { Say $_ }
if ($LASTEXITCODE -ne 0) { Say "schema generation FAILED"; exit 1 }

# 5. Build gate.
dotnet build (Join-Path $repo "AlbionPacketExplorer.slnx") -clp:ErrorsOnly -nologo | ForEach-Object { Say $_ }
if ($LASTEXITCODE -ne 0) { Say "BUILD FAILED - changes left uncommitted"; exit 1 }

# 6. Report + optional commit (never pushes).
$paths = @("tools/inferred-overlay.json", "tools/digest-overlay.json",
           "tools/digest-worker/digests", "src/AlbionPacketExplorer/Assets/packet-schema.base.json")
$changed = git -C $repo status --porcelain -- $paths
if (-not $changed) {
    Say "Schema unchanged. Done."
    exit 0
}
Say "Schema changed:"
git -C $repo diff --stat -- $paths | ForEach-Object { Say $_ }

if ($Commit) {
    git -C $repo add -- $paths
    git -C $repo commit -m "packet(app): fold captures and shared digests into schema"
    Say "Committed (NOT pushed)."
} else {
    Say "Left uncommitted for review. Re-run with -Commit to commit."
}
Say "=== digest sync done ==="
