[CmdletBinding()]
param(
    [string]$Output,
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path $MyInvocation.MyCommand.Path -Parent
if([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $scriptDirectory '..\build'
}
$Output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$iconFile = & (Join-Path $PSScriptRoot 'GenerateIcon.ps1') -Output $Output
if($Architecture -eq 'arm64') {
    # The in-box .NET Framework csc predates /platform:arm64. Roslyn from the
    # .NET SDK targets the real ARM64 PE machine, with official 4.8.1 refs.
    & (Join-Path $PSScriptRoot '..\scripts\windows\Invoke-Arm64Build.ps1') `
        -Project (Join-Path $PSScriptRoot 'Yarnelo.Arm64.csproj') -OutputDirectory $Output -IconPath $iconFile
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Yarnelo.arm64.exe.config') -Destination (Join-Path $Output 'Yarnelo.exe.config') -Force
} else {
    $frameworkFolder = 'Framework'
    if($Architecture -eq 'x64') { $frameworkFolder = 'Framework64' }
    $framework = Join-Path $env:WINDIR ('Microsoft.NET\' + $frameworkFolder + '\v4.0.30319')
    $compiler = Join-Path $framework 'csc.exe'
    if(!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
        throw "The .NET Framework compiler for $Architecture was not found. Build on Windows with .NET Framework 4.8."
    }
    $sources = @('Program.cs','DockWindow.cs','SettingsWindow.cs','Native.cs','DesktopHost.cs','SmoothMotion.cs','Store.cs','Localization.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    $compilerArgs = @('/nologo','/target:winexe',('/platform:'+$Architecture),'/optimize+','/codepage:65001',('/out:'+(Join-Path $Output 'Yarnelo.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')),('/win32icon:'+$iconFile),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xaml.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll',('/reference:'+(Join-Path $framework 'WPF\WindowsBase.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationCore.dll')),('/reference:'+(Join-Path $framework 'WPF\PresentationFramework.dll')))
    & $compiler @compilerArgs @sources
    if($LASTEXITCODE -ne 0) { throw 'Не удалось собрать приложение' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Yarnelo.exe.config') -Destination $Output -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'portable.mode') -Destination $Output -Force
$guide = Join-Path $PSScriptRoot '..\docs\USER_GUIDE.ru.txt'
if(Test-Path -LiteralPath $guide -PathType Leaf) {
    Copy-Item -LiteralPath $guide -Destination (Join-Path $Output 'Прочитай меня.txt') -Force
}
$englishGuide = Join-Path $PSScriptRoot '..\docs\USER_GUIDE.en.txt'
if(Test-Path -LiteralPath $englishGuide -PathType Leaf) {
    Copy-Item -LiteralPath $englishGuide -Destination (Join-Path $Output 'Read me.txt') -Force
}
Write-Output (Join-Path $Output 'Yarnelo.exe')
