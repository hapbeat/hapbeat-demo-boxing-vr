# Links workspace-only sources into this project. A standalone clone does not need this script:
# packages resolve from the git URLs in Packages/manifest.json and the menu hands use the public placeholder.
# Idempotent: existing correct links are kept, a missing target skips its link.
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$demos = Join-Path $project '../..'
$links = @(
    @{ Path = 'Assets/HapbeatPrivate'; Target = Join-Path $demos 'private-assets/unity/boxing-vr/HapbeatPrivate'; Note = 'private Unity XR Hands meshes' },
    @{ Path = 'Packages/com.hapbeat.sdk'; Target = Join-Path $demos '../repos-sdk/hapbeat-unity-sdk'; Note = 'embedded live Hapbeat Unity SDK' },
    @{ Path = 'Packages/com.hapbeat.demo-switch'; Target = Join-Path $demos 'unity/packages/com.hapbeat.demo-switch'; Note = 'embedded live Demo Switch package' }
)
foreach ($link in $links) {
    $path = Join-Path $project $link.Path
    if (-not (Test-Path -LiteralPath $link.Target)) { Write-Output "SKIP  $($link.Path): target not found ($($link.Target))"; continue }
    $target = (Resolve-Path -LiteralPath $link.Target).Path
    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($item) {
        if ($item.LinkType -eq 'Junction' -and (Resolve-Path -LiteralPath $item.Target).Path -eq $target) { Write-Output "OK    $($link.Path) -> $target"; continue }
        throw "$($link.Path) exists and is not a junction to $target. Move it aside manually; this script never deletes files."
    }
    New-Item -ItemType Junction -Path $path -Target $target | Out-Null
    Write-Output "LINK  $($link.Path) -> $target ($($link.Note))"
}
Write-Output 'Close and reopen Unity (or let it refresh) so the linked assets and embedded packages are imported.'
