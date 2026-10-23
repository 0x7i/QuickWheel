[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = 'dist',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repositoryRoot 'RadialCommandWheel.sln'
$testProject = Join-Path $repositoryRoot 'tests\RadialCommandWheel.Tests\RadialCommandWheel.Tests.csproj'
$appProject = Join-Path $repositoryRoot 'src\RadialCommandWheel\RadialCommandWheel.csproj'
$toolProject = Join-Path $repositoryRoot 'src\RadialCommandWheel.Tools\RadialCommandWheel.Tools.csproj'
$iconPath = Join-Path $repositoryRoot 'src\RadialCommandWheel\assets\app.ico'
$output = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $repositoryRoot $OutputDirectory
}

$dotnet = if ($env:DOTNET_EXE) {
    $env:DOTNET_EXE
} elseif (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") {
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
} else {
    (Get-Command dotnet -ErrorAction Stop).Source
}

$artifacts = Join-Path $repositoryRoot '.artifacts'
$cliHome = Join-Path $artifacts 'dotnet-home'
$packageCache = Join-Path $artifacts 'nuget-packages'
New-Item -ItemType Directory -Force -Path $cliHome, $packageCache, $output | Out-Null
$env:DOTNET_CLI_HOME = $cliHome
$env:NUGET_PACKAGES = $packageCache
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $repositoryRoot
try {
    & $dotnet restore $solution --configfile (Join-Path $repositoryRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }

    & $dotnet build $toolProject -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool project build failed.' }

    & $dotnet run --project $toolProject -c $Configuration --no-build -- generate-icon --output $iconPath |
        Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }

    & $dotnet build $solution -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

    if (-not $SkipTests) {
        & $dotnet run --project $testProject -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }
    }

    & $dotnet publish $appProject -c $Configuration -r $Runtime --self-contained true --no-restore `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false `
        -o $output
    if ($LASTEXITCODE -ne 0) { throw 'Single-file publish failed.' }

    $executable = Join-Path $output 'RadialCommandWheel.exe'
    if (-not (Test-Path $executable)) {
        throw "Publish completed without the expected executable: $executable"
    }

    Get-Item $executable | Select-Object FullName, Length, LastWriteTime
    Get-FileHash $executable -Algorithm SHA256 | Select-Object Algorithm, Hash
}
finally {
    Pop-Location
}

