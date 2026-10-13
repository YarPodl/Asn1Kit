# Configures, builds, and tests the native runtime with one CMake preset.
[CmdletBinding()]
param(
    [ValidateSet('fetch-default', 'conan-default', 'conan-release', 'conan-default-vs')]
    [string] $Preset = 'fetch-default'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repositoryRoot 'runtime-cpp'
$runningOnWindows = $env:OS -eq 'Windows_NT'

function Find-CMake {
    $command = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    if (-not $runningOnWindows) {
        throw 'cmake was not found in PATH.'
    }

    $visualStudioRoot = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio/2022'
    foreach ($edition in @('Community', 'Professional', 'Enterprise', 'BuildTools')) {
        $candidate = Join-Path $visualStudioRoot "$edition/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw 'cmake was not found in PATH or Visual Studio 2022. Install CMake >= 3.21 and Ninja.'
}

function Invoke-NativeTool {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path -Leaf $Executable) failed with exit code $LASTEXITCODE."
    }
}

$cmake = Find-CMake
$cmakeBin = Split-Path -Parent $cmake
$ctest = Join-Path $cmakeBin $(if ($runningOnWindows) { 'ctest.exe' } else { 'ctest' })
if (-not (Test-Path -LiteralPath $ctest)) {
    $ctestCommand = Get-Command ctest -ErrorAction SilentlyContinue
    if ($null -eq $ctestCommand) {
        throw "ctest was not found next to CMake or in PATH. CMake: $cmake"
    }
    $ctest = $ctestCommand.Source
}

if ($cmake -like '*Microsoft Visual Studio*') {
    $ninjaDirectory = Join-Path (Split-Path -Parent (Split-Path -Parent $cmakeBin)) 'Ninja'
    if (Test-Path -LiteralPath (Join-Path $ninjaDirectory 'ninja.exe')) {
        $env:Path = "$ninjaDirectory$([IO.Path]::PathSeparator)$env:Path"
    }
}

$conanToolchains = @{
    'conan-default' = 'build/conan_toolchain.cmake'
    'conan-release' = 'build-release/conan_toolchain.cmake'
    'conan-default-vs' = 'build/conan_toolchain.cmake'
}
if ($conanToolchains.ContainsKey($Preset)) {
    $toolchain = Join-Path $runtimeRoot $conanToolchains[$Preset]
    if (-not (Test-Path -LiteralPath $toolchain)) {
        $buildType = if ($Preset -eq 'conan-release') { 'Release' } else { 'Debug' }
        $outputDirectory = if ($Preset -eq 'conan-release') { 'build-release' } else { 'build' }
        throw "Conan toolchain is missing: $toolchain. From runtime-cpp run: conan install . -s build_type=$buildType -of $outputDirectory --build=missing"
    }
}

$ninja = Get-Command ninja -ErrorAction SilentlyContinue
Write-Host "CMake: $cmake"
Write-Host "CTest: $ctest"
if ($null -ne $ninja) { Write-Host "Ninja: $($ninja.Source)" }
Write-Host "Preset: $Preset"

Push-Location $runtimeRoot
try {
    Invoke-NativeTool $cmake @('--preset', $Preset)
    Invoke-NativeTool $cmake @('--build', '--preset', $Preset)
    Invoke-NativeTool $ctest @('--preset', $Preset, '--no-tests=error')
}
finally {
    Pop-Location
}
