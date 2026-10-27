[CmdletBinding()]
param([string]$OutputDirectory,[ValidateSet('x64','x86','arm64')][string]$Architecture='x64')
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(!$OutputDirectory){$OutputDirectory=Join-Path $repository 'work\secure-save-tests'}
$output=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
for($ancestor=$output;$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)){
 if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Strict-save tests cannot run through a reparse point'}
}
New-Item -ItemType Directory -Path $output -Force|Out-Null
$run=Join-Path $output ([Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run|Out-Null
$binary=Join-Path $run 'SecureSaveTests.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$platform=if($Architecture -eq 'arm64'){'anycpu'}else{$Architecture}
& $compiler /nologo /target:exe ('/platform:'+$platform) /optimize+ /codepage:65001 ('/out:'+$binary) /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Runtime.Serialization.dll (Join-Path $repository 'src\Store.cs') (Join-Path $repository 'src\Localization.cs') (Join-Path $repository 'src\SecureSaveTests.cs')
if($LASTEXITCODE -ne 0){throw 'Strict-save test compilation failed'}
& $binary (Join-Path $run 'fixtures') $Architecture
if($LASTEXITCODE -ne 0){throw 'Strict-save tests failed'}
Write-Host ('Isolated strict-save fixtures: '+$run)
