<#
.SYNOPSIS
    Publishes the shard's edited map files so every player's launcher downloads them.

.DESCRIPTION
    Copies staticsN.mul and staidxN.mul for each map from the live server's ClientFiles and
    uploads them to the "shard-data" GitHub release. The launcher downloads that release into
    TazUO\ShardData on the next Play and starts TazUO with those files in place of EA's.

    Run it after changing the map in-game ([Unfreeze, delete or add trees, [Freeze).
    Needs the GitHub CLI (gh), logged in to an account that can write to the launcher repo.

.PARAMETER Maps
    Map numbers to publish: 0 Felucca, 1 Trammel, 2 Ilshenar, 3 Malas, 4 Tokuno, 5 Ter Mur.

.PARAMETER Remove
    Takes these maps' files off the release instead, so players go back to EA's stock files.
    Do this only if the server's files are back to stock too.

.EXAMPLE
    .\Publish-ShardData.ps1 -Maps 0,1

.EXAMPLE
    .\Publish-ShardData.ps1 -Maps 2 -Remove
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateRange(0, 5)]
    [int[]] $Maps,

    [string] $Source = '\\Phoenix-Server\GameServers\ServUO\ClientFiles',

    [switch] $Remove
)

$ErrorActionPreference = 'Stop'
$repo = 'Brandin-Canterbury/SeriousFunLauncher'
$tag = 'shard-data'

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'The GitHub CLI (gh) is not installed. Get it from https://cli.github.com/.'
}

$names = foreach ($map in $Maps) { "statics$map.mul"; "staidx$map.mul" }

# Create the release on first use. --latest=false keeps the launcher's own release as "latest",
# which is what the launcher's download link and the Release workflow rely on.
gh release view $tag --repo $repo *> $null
if ($LASTEXITCODE -ne 0) {
    if ($Remove) {
        Write-Host "There is no $tag release, so there is nothing to remove."
        return
    }

    if ($PSCmdlet.ShouldProcess("$repo", "create release $tag")) {
        gh release create $tag --repo $repo --title 'Shard map data' --latest=false `
            --notes 'Edited map files for the SeriousFun shard. The launcher downloads these automatically; there is nothing to install by hand.'
        if ($LASTEXITCODE -ne 0) { throw "Creating the $tag release failed." }
    }
}

if ($Remove) {
    foreach ($name in $names) {
        if ($PSCmdlet.ShouldProcess("$tag/$name", 'delete asset')) {
            gh release delete-asset $tag $name --repo $repo --yes
        }
    }
    return
}

# Copy first: the server keeps these files open, and a local copy is a consistent snapshot to upload.
$staging = Join-Path ([IO.Path]::GetTempPath()) ("shard-data-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null

try {
    foreach ($name in $names) {
        $from = Join-Path $Source $name
        if (-not (Test-Path $from)) { throw "$from doesn't exist." }
        Copy-Item $from $staging
    }

    $files = $names | ForEach-Object { Join-Path $staging $_ }
    foreach ($file in $files) {
        $item = Get-Item $file
        Write-Host ("{0,-14} {1,8:N1} MB  {2}" -f $item.Name, ($item.Length / 1MB), (Get-FileHash $file -Algorithm SHA256).Hash)
    }

    if ($PSCmdlet.ShouldProcess("$tag", "upload $($names -join ', ')")) {
        # --clobber replaces same-named assets; the new asset ids make every launcher re-download.
        gh release upload $tag @files --repo $repo --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Upload failed.' }
        Write-Host "Published. Players get the new maps the next time they press Play."
    }
}
finally {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
}
