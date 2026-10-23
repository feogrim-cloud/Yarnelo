[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$IconPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if(!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Native ARM64 compilation requires the .NET 8 SDK or newer (https://dotnet.microsoft.com/download). The in-box Framework compiler cannot target ARM64.'
}
$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$projectFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Project)
$intermediate = Join-Path $repository ('work\arm64-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $output, $intermediate | Out-Null
# Keep all restore/build intermediates out of the source tree and out of the
# clean payload. The package reference pins Microsoft's official 4.8.1 refs.
$arguments = @('build',$projectFile,'--configuration','Release','--nologo','--verbosity','minimal',
    ('/p:OutputPath=' + $output + '\'),('/p:BaseIntermediateOutputPath=' + $intermediate + '\'),
    ('/p:MSBuildProjectExtensionsPath=' + $intermediate + '\'),
    '/p:RestoreSources=https://api.nuget.org/v3/index.json')
if($IconPath) { $arguments += '/p:YarneloIconPath=' + $IconPath }
& dotnet @arguments
if($LASTEXITCODE -ne 0) { throw 'Roslyn/.NET Framework 4.8.1 ARM64 compilation failed.' }
[xml]$projectXml = [IO.File]::ReadAllText($projectFile)
$assemblyName = [string]$projectXml.Project.PropertyGroup.AssemblyName
$executable = Join-Path $output ($assemblyName + '.exe')
if(!(Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The ARM64 project did not generate its executable.' }
$bytes = [IO.File]::ReadAllBytes($executable)
if($bytes.Length -lt 64) { throw 'The ARM64 output has no PE header.' }
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
if($peOffset -lt 0 -or $peOffset + 26 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550 -or
   [BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0xaa64) {
    throw 'The compiler output must be a real ARM64 PE executable (machine 0xaa64).'
}
Write-Host ('Verified native ARM64 PE: ' + $executable)
