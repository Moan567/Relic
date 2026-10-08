#!/usr/bin/env pwsh
param(
	[string]$LocalPath
)

$dest = Join-Path -Path $PSScriptRoot -ChildPath "..\thirdparty\TrenchBroom" | Resolve-Path -ErrorAction SilentlyContinue
if ($LocalPath) {
	if (-Not (Test-Path $LocalPath)) {
		Write-Error "Local path '$LocalPath' does not exist."
		exit 1
	}
	$destPath = Join-Path -Path $PSScriptRoot -ChildPath "..\thirdparty\TrenchBroom"
	if (Test-Path $destPath) { Write-Host "Destination '$destPath' already exists. Aborting."; exit 1 }
	Write-Host "Copying TrenchBroom from local path '$LocalPath' to '$destPath'..."
	Copy-Item -Path $LocalPath -Destination $destPath -Recurse -Force
	Write-Host "Local TrenchBroom copied to thirdparty/TrenchBroom."
	Write-Host "Build TrenchBroom by following thirdparty/TrenchBroom/README.md"
	exit 0
}

Write-Host "Adding TrenchBroom as a git submodule into thirdparty/TrenchBroom"
git submodule add https://github.com/kduske/TrenchBroom.git thirdparty/TrenchBroom
git submodule update --init --recursive
Write-Host "Submodule added. To build TrenchBroom follow the project's build instructions in thirdparty/TrenchBroom/README.md"
