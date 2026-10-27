[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'build\uninstall-profile-tests'
}
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
for($ancestor=$output; $ancestor; $ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
    if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'Synthetic uninstall tests must not run through a reparse point.'
    }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$run = Join-Path $output ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$executable = Join-Path $run 'UninstallProfileTests.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001 "/out:$executable" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Runtime.Serialization.dll (Join-Path $PSScriptRoot 'UninstallProfile.cs') (Join-Path $PSScriptRoot 'UninstallProfileTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Uninstall profile test compilation failed.' }
& $executable (Join-Path $run 'fixtures')
if($LASTEXITCODE -ne 0) { throw 'Uninstall profile regression tests failed.' }
Write-Host "Preserved synthetic uninstall fixtures: $run"
