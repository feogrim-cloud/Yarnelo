param([string]$Output)
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path $MyInvocation.MyCommand.Path -Parent
if([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $scriptDirectory '..\build'
}
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$iconFile = & (Join-Path $PSScriptRoot 'GenerateIcon.ps1') -Output $Output
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sources = @('Program.cs','DockWindow.cs','SettingsWindow.cs','Native.cs','DesktopHost.cs','SmoothMotion.cs','Store.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:'+(Join-Path $Output 'Yarnelo.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+$iconFile),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',('/reference:'+(Join-Path $framework 'WPF\WindowsBase.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationCore.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationFramework.dll')))
& (Join-Path $framework 'csc.exe') @compilerArgs @sources
if($LASTEXITCODE -ne 0) { throw 'Не удалось собрать приложение' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Yarnelo.exe.config') -Destination $Output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'portable.mode') -Destination $Output -Force
$guide = Join-Path $PSScriptRoot '..\docs\USER_GUIDE.ru.txt'
if(Test-Path -LiteralPath $guide -PathType Leaf) {
    Copy-Item -LiteralPath $guide -Destination (Join-Path $Output 'Прочитай меня.txt') -Force
}
Write-Output (Join-Path $Output 'Yarnelo.exe')
