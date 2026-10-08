#!/usr/bin/env pwsh
param(
	[Parameter(Mandatory=$true)]
	[string]$SourceGamesPath
)

$destPath = Join-Path -Path $PSScriptRoot -ChildPath "..\thirdparty\TrenchBroom\games"

if (-Not (Test-Path $SourceGamesPath)) {
	Write-Error "Source games path '$SourceGamesPath' does not exist."
	exit 1
}

if (-Not (Test-Path $destPath)) {
	New-Item -ItemType Directory -Path $destPath -Force | Out-Null
}

Write-Host "Copying games from '$SourceGamesPath' to '$destPath'..."
Copy-Item -Path (Join-Path $SourceGamesPath '*') -Destination $destPath -Recurse -Force
Write-Host "Done. Please inspect thirdparty/TrenchBroom/games for the copied content."
