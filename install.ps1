# Publish Vakage into C:\TKHN\Vakage and put it on the Start Menu and the
# user PATH, through the Tekhnexos companion family scripts in L:\Sync.
# Settings and logs stay under %LOCALAPPDATA%\Vakage.
#
# Outside that family (anyone else building Vakage), publish it directly:
#   dotnet publish src\Vakage\Vakage.csproj -c Release -o publish
#
# Usage:
#   .\install.bat

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$sync = 'L:\Sync'
if (-not (Test-Path -LiteralPath (Join-Path $sync 'CompanionFamily.ps1'))) {
    $sync = Join-Path $repo '..\..\..\..\Sync'
}
if (-not (Test-Path -LiteralPath (Join-Path $sync 'CompanionFamily.ps1'))) {
    throw "The companion family scripts (L:\Sync) are not on this machine. Publish with: dotnet publish src\Vakage\Vakage.csproj -c Release -o publish"
}

. (Join-Path $sync 'CompanionFamily.ps1')

$null = Publish-CompanionApp `
    -App Vakage `
    -Project (Join-Path $repo 'src\Vakage\Vakage.csproj') `
    -RepoRoot $repo `
    -SelfContained
