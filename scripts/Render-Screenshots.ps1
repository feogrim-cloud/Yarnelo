[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [string]$Output
)
$ErrorActionPreference='Stop'
$repository=Split-Path $PSScriptRoot -Parent
if(!$Output) { $Output=Join-Path $repository 'assets\screenshots' }
$run=Join-Path $repository ('work\screenshots-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$binary=Join-Path $run 'ScreenshotRenderer.exe'
$arguments=@('/nologo','/target:exe','/platform:x64','/optimize+','/codepage:65001','/main:Polka.ScreenshotRenderer',('/out:'+$binary),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',('/reference:'+(Join-Path $framework 'WPF\WindowsBase.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationCore.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationFramework.dll')))
$sources=@('Program.cs','DockWindow.cs','DockLayout.cs','SettingsWindow.cs','Native.cs','DesktopHost.cs','SmoothMotion.cs','Store.cs','Localization.cs') | ForEach-Object {Join-Path $repository ('src\'+$_)}
& (Join-Path $framework 'csc.exe') @arguments @sources (Join-Path $PSScriptRoot 'ScreenshotRenderer.cs')
if($LASTEXITCODE -ne 0) { throw 'Screenshot renderer compilation failed.' }
Copy-Item -LiteralPath (Join-Path $repository 'src\Yarnelo.exe.config') -Destination ($binary+'.config')
& $binary $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Manifest) $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output) (Join-Path $run 'fixtures')
if($LASTEXITCODE -ne 0) { throw 'Screenshot rendering failed.' }
