[CmdletBinding()]
param(
    [string]$ScratchRoot,
    [switch]$CompileUiTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptDirectory = Split-Path $MyInvocation.MyCommand.Path -Parent
$repository = Split-Path $scriptDirectory -Parent
if([string]::IsNullOrWhiteSpace($ScratchRoot)) {
    $ScratchRoot = Join-Path $repository 'work\test-runs'
}
$sources = Join-Path $repository 'src'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if(!(Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The 64-bit .NET Framework C# compiler was not found. Run this script on Windows with .NET Framework 4.8.'
}

# Never reuse a test profile or point the fixture runner at the real desktop.
# Keep the files after the run so failed transactions can be inspected safely.
$scratch = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScratchRoot)
for($ancestor = $scratch; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
    if((Test-Path -LiteralPath $ancestor) -and
       ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "A test directory must not be inside a reparse point: $ancestor"
    }
}
$run = Join-Path $scratch ('storage-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
if(Test-Path -LiteralPath $run) { throw 'The new isolated test directory already exists.' }
New-Item -ItemType Directory -Path $run -Force | Out-Null
$binary = Join-Path $run 'StorageTests.exe'
$compilerArgs = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001',
    ('/out:' + $binary),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Runtime.Serialization.dll'
)
& $compiler @compilerArgs (Join-Path $sources 'Store.cs') (Join-Path $sources 'Localization.cs') (Join-Path $sources 'StorageTests.cs')
if($LASTEXITCODE -ne 0) { throw 'Storage test compilation failed.' }

# Every fixture writes below this new path; Store receives the fixture desktop
# explicitly. No dock, installer, registry helper or existing profile is started.
$fixtures = Join-Path $run 'fixtures'
& $binary $fixtures
if($LASTEXITCODE -ne 0) { throw "Storage tests failed. Fixtures are preserved at $fixtures" }

# Localization renders only its own unshown WPF visual trees. No native dock,
# desktop integration, tray, installed profile or startup registry is used.
$localizationSources = @('Program.cs','DockWindow.cs','SettingsWindow.cs','Native.cs','DesktopHost.cs','SmoothMotion.cs','Store.cs','Localization.cs','LocalizationUiTests.cs') |
    ForEach-Object { Join-Path $sources $_ }
$localizationBinary = Join-Path $run 'LocalizationUiTests.exe'
$localizationArgs = @(
    '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001',
    '/main:Polka.LocalizationUiTests', ('/out:' + $localizationBinary),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Runtime.Serialization.dll',
    '/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    ('/reference:' + (Join-Path $framework 'WPF\WindowsBase.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path $framework 'WPF\PresentationFramework.dll'))
)
& $compiler @localizationArgs @localizationSources
if($LASTEXITCODE -ne 0) { throw 'Localization UI test compilation failed.' }
& $localizationBinary (Join-Path $run 'localization-ui')
if($LASTEXITCODE -ne 0) { throw 'Localization UI tests failed.' }

if($CompileUiTests) {
    # Compile only: UI tests show and activate native WPF windows when executed.
    $uiSources = @('Program.cs','DockWindow.cs','SettingsWindow.cs','Native.cs','DesktopHost.cs','SmoothMotion.cs','Store.cs','Localization.cs','UiTests.cs') |
        ForEach-Object { Join-Path $sources $_ }
    $uiArgs = @(
        '/nologo', '/target:exe', '/platform:x64', '/optimize+', '/codepage:65001',
        '/main:Polka.UiTests', ('/out:' + (Join-Path $run 'UiTests.exe')),
        '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Runtime.Serialization.dll',
        '/reference:System.Xaml.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
        ('/reference:' + (Join-Path $framework 'WPF\WindowsBase.dll')),
        ('/reference:' + (Join-Path $framework 'WPF\PresentationCore.dll')),
        ('/reference:' + (Join-Path $framework 'WPF\PresentationFramework.dll'))
    )
    & $compiler @uiArgs @uiSources
    if($LASTEXITCODE -ne 0) { throw 'UI test compilation failed.' }
    Write-Host 'UI tests compiled successfully; no UI tests or dock windows were launched.'
}
Write-Host "Tests finished. Isolated files: $run"
