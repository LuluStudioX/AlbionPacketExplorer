# Pulls every stored digest from the DIGESTS KV namespace into ./digests/ as one JSON file
# per submission, then prints a count. Run from tools/digest-worker. Requires wrangler auth.
#
#   .\pull-digests.ps1            # fetch all
#   .\pull-digests.ps1 -Delete    # fetch all, then delete the fetched keys from KV

param(
    [switch] $Delete
)

$outDir = Join-Path $PSScriptRoot "digests"
New-Item -ItemType Directory -Force $outDir | Out-Null

# cmd-level stderr redirect: wrangler prints config warnings to stderr, and PowerShell 5.1
# would wrap those in ErrorRecords if redirected at the PS level.
$raw = cmd /c "npx wrangler kv key list --binding DIGESTS --remote 2>nul" | Out-String
$keys = $raw | ConvertFrom-Json

if (-not $keys -or $keys.Count -eq 0) {
    Write-Host "No digests stored."
    exit 0
}

$fetched = 0
foreach ($k in $keys) {
    # Key: d:<schemaCommit>:<mode>:<sha256> -> file: <schemaCommit>-<mode>-<hash12>.json
    $parts = $k.name -split ":"
    $file = "$($parts[1])-$($parts[2])-$($parts[3].Substring(0, 12)).json"
    $path = Join-Path $outDir $file

    $body = cmd /c "npx wrangler kv key get ""$($k.name)"" --binding DIGESTS --remote 2>nul" | Out-String
    [IO.File]::WriteAllText($path, $body.Trim(), (New-Object System.Text.UTF8Encoding $false))
    $fetched++
    Write-Host "  $file  (app $($k.metadata.app), $($k.metadata.codes) codes, $($k.metadata.receivedAt))"

    if ($Delete) {
        cmd /c "npx wrangler kv key delete ""$($k.name)"" --binding DIGESTS --remote 2>nul" | Out-Null
    }
}

Write-Host "Fetched $fetched digest(s) into $outDir$(if ($Delete) { ', deleted from KV' })."
