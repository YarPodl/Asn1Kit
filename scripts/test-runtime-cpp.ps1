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

function Find-VisualStudioInstallation {
    if (-not $runningOnWindows) {
        return $null
    }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $installationPath = & $vswhere `
            -latest `
            -products '*' `
            -version '[17.0,18.0)' `
            -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -property installationPath
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($installationPath)) {
            return [string] ($installationPath | Select-Object -First 1)
        }
    }

    $visualStudioRoot = Join-Path ${env:ProgramFiles} 'Microsoft Visual Studio/2022'
    foreach ($edition in @('Community', 'Professional', 'Enterprise', 'BuildTools')) {
        $candidate = Join-Path $visualStudioRoot $edition
        if (Test-Path -LiteralPath (Join-Path $candidate 'Common7/Tools/VsDevCmd.bat')) {
            return $candidate
        }
    }

    return $null
}

function Initialize-MsvcEnvironment {
    param([AllowNull()] [string] $VisualStudioInstallation)

    if (-not $runningOnWindows) {
        return
    }

    $cl = Get-Command cl.exe -ErrorAction SilentlyContinue
    if ($null -ne $cl -and
        -not [string]::IsNullOrWhiteSpace($env:INCLUDE) -and
        -not [string]::IsNullOrWhiteSpace($env:LIB)) {
        Write-Host "MSVC environment: already initialized ($($cl.Source))"
        return
    }

    if ([string]::IsNullOrWhiteSpace($VisualStudioInstallation)) {
        throw 'Visual Studio 2022 with the MSVC x64 tools was not found. Install the Microsoft.VisualStudio.Component.VC.Tools.x86.x64 component.'
    }

    $vsDevCmd = Join-Path $VisualStudioInstallation 'Common7/Tools/VsDevCmd.bat'
    if (-not (Test-Path -LiteralPath $vsDevCmd)) {
        throw "Visual Studio developer environment script was not found: $vsDevCmd"
    }

    $environmentLines = & $env:ComSpec /d /s /c "call `"$vsDevCmd`" -no_logo -arch=x64 -host_arch=x64 >nul && set"
    if ($LASTEXITCODE -ne 0) {
        throw "VsDevCmd.bat failed to initialize the MSVC x64 environment with exit code $LASTEXITCODE."
    }

    foreach ($line in $environmentLines) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) {
            continue
        }

        $name = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }

    $cl = Get-Command cl.exe -ErrorAction SilentlyContinue
    if ($null -eq $cl -or
        [string]::IsNullOrWhiteSpace($env:INCLUDE) -or
        [string]::IsNullOrWhiteSpace($env:LIB)) {
        throw 'VsDevCmd.bat completed, but the MSVC x64 environment is incomplete: cl.exe, INCLUDE, or LIB is missing.'
    }

    Write-Host "MSVC environment: initialized ($($cl.Source))"
}

function Find-CMake {
    param([AllowNull()] [string] $VisualStudioInstallation)

    $command = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    if (-not $runningOnWindows) {
        throw 'cmake was not found in PATH.'
    }

    if (-not [string]::IsNullOrWhiteSpace($VisualStudioInstallation)) {
        $candidate = Join-Path $VisualStudioInstallation 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
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

$visualStudioInstallation = Find-VisualStudioInstallation
Initialize-MsvcEnvironment $visualStudioInstallation

$cmake = Find-CMake $visualStudioInstallation
$cmakeBin = Split-Path -Parent $cmake
$ctest = Join-Path $cmakeBin $(if ($runningOnWindows) { 'ctest.exe' } else { 'ctest' })
if (-not (Test-Path -LiteralPath $ctest)) {
    $ctestCommand = Get-Command ctest -ErrorAction SilentlyContinue
    if ($null -eq $ctestCommand) {
        throw "ctest was not found next to CMake or in PATH. CMake: $cmake"
    }
    $ctest = $ctestCommand.Source
}

if ($runningOnWindows -and -not [string]::IsNullOrWhiteSpace($visualStudioInstallation)) {
    $ninjaDirectory = Join-Path $visualStudioInstallation 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja'
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
