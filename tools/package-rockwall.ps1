param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "dist\Rockwall-win-x64"
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$outputParent = Split-Path -Parent $OutputDirectory
$outputName = Split-Path -Leaf $OutputDirectory
if (Test-Path $OutputDirectory) {
    throw "Output directory already exists: $OutputDirectory. Choose another output path or remove it yourself before packaging."
}

New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
$staging = Join-Path $outputParent ".$outputName-$([guid]::NewGuid().ToString('N')).tmp"
$editorProject = Join-Path $repoRoot "Rockwall2\Rockwall2.csproj"
$gameProject = Join-Path $repoRoot "Game\Game.csproj"
$compilerProject = Join-Path $repoRoot "Editor\Mapcompiler\Mapcompiler.csproj"

function Invoke-Dotnet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

try {
    New-Item -ItemType Directory -Path $staging | Out-Null
    $gameOutput = Join-Path $staging "Game"
    $compilerOutput = Join-Path $staging "Mapcompiler"

    Invoke-Dotnet @("publish", $editorProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", $staging)
    Invoke-Dotnet @("publish", $gameProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", $gameOutput)
    Invoke-Dotnet @("publish", $compilerProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", $compilerOutput)

    Copy-Item -Path (Join-Path $repoRoot "Game\Working") -Destination (Join-Path $gameOutput "Working") -Recurse

    $gameExecutable = Join-Path $gameOutput "Game.exe"
    if (-not (Test-Path $gameExecutable)) {
        throw "Published game executable not found: $gameExecutable"
    }

    $compileProcess = Start-Process -FilePath $gameExecutable `
        -ArgumentList @("-compile", "-batch") `
        -WorkingDirectory $gameOutput `
        -NoNewWindow `
        -Wait `
        -PassThru
    if ($compileProcess.ExitCode -ne 0) {
        throw "Game entity-data generation failed with exit code $($compileProcess.ExitCode)."
    }

    if (-not (Test-Path (Join-Path $gameOutput "Data\def.eds"))) {
        throw "The game did not generate Data\def.eds; package was not completed."
    }

    foreach ($license in @("LICENSE.txt", "Relic Engine License 1.0.txt", "THIRD_PARTY_LICENSES.txt.txt")) {
        Copy-Item (Join-Path $repoRoot $license) -Destination $staging
    }

    Move-Item -Path $staging -Destination $OutputDirectory
    Write-Host "Package created: $OutputDirectory"
}
catch {
    Write-Error "Packaging failed. Intermediate files were left at '$staging' for inspection. $($_.Exception.Message)"
    throw
}