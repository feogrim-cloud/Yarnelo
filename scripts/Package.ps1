[CmdletBinding()]
param(
    [string]$Output,
    [string]$ScratchRoot,
    [ValidateSet('x64', 'x86')]
    [string]$Architecture = 'x64',
    [switch]$BuildInstaller,
    [string]$IsccPath,
    [string]$InstallerOutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scriptDirectory = Split-Path $MyInvocation.MyCommand.Path -Parent
$repository = Split-Path $scriptDirectory -Parent
if([string]::IsNullOrWhiteSpace($Output)) {
    $Output = Join-Path $repository ('dist\Yarnelo-Windows-' + $Architecture + '-Portable.zip')
}
if([string]::IsNullOrWhiteSpace($ScratchRoot)) {
    $ScratchRoot = Join-Path $repository 'work\package-runs'
}
$archive = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
if(Test-Path -LiteralPath $archive) { throw "The archive already exists. Choose a new output path: $archive" }
$scratch = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScratchRoot)
foreach($candidate in @($scratch, [IO.Path]::GetDirectoryName($archive))) {
    for($ancestor = $candidate; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and
           ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "A package directory must not be inside a reparse point: $ancestor"
        }
    }
}
$stage = Join-Path $scratch ('package-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
if(Test-Path -LiteralPath $stage) { throw 'The new package directory already exists.' }
New-Item -ItemType Directory -Path $stage -Force | Out-Null
& (Join-Path $repository 'src\Build.ps1') -Output $stage -Architecture $Architecture

# Package only files produced by a fresh build, never a user's portable profile,
# imported shortcuts, debug dumps, test fixtures or an installed application.
$required = @('Yarnelo.exe','Yarnelo.exe.config','Yarnelo.ico','portable.mode','Прочитай меня.txt','Read me.txt','Yarnelo-Icon.svg','Yarnelo-Icon.png')
foreach($name in $required) {
    if(!(Test-Path -LiteralPath (Join-Path $stage $name) -PathType Leaf)) { throw "Missing package file: $name" }
}
$allowed = '^(Yarnelo\.exe|Yarnelo\.exe\.config|Yarnelo\.ico|Yarnelo-\d+\.\d+\.\d+\.ico|portable\.mode|Прочитай меня\.txt|Read me\.txt|Yarnelo-Icon(?:-(?:16|24|32|48|256))?\.png|Yarnelo-Icon\.svg)$'
foreach($entry in Get-ChildItem -LiteralPath $stage -Force) {
    if($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $entry.Name -notmatch $allowed) {
        throw "Unexpected file in clean package directory: $($entry.Name)"
    }
}
foreach($name in @('LICENSE','README.md','README.ru.md')) {
    $document = Join-Path $repository $name
    if(Test-Path -LiteralPath $document -PathType Leaf) { Copy-Item -LiteralPath $document -Destination $stage }
}
foreach($document in @(@('WINDOWS.md','INSTALL-WINDOWS.md'),@('WINDOWS.ru.md','INSTALL-WINDOWS.ru.md'))) {
    $windowsGuide = Join-Path $repository ('docs\'+$document[0])
    if(!(Test-Path -LiteralPath $windowsGuide -PathType Leaf)) { throw 'A Windows installation guide is missing.' }
    Copy-Item -LiteralPath $windowsGuide -Destination (Join-Path $stage $document[1])
}
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($archive)) -Force | Out-Null
$files = @(Get-ChildItem -LiteralPath $stage -File | ForEach-Object { $_.FullName })
Compress-Archive -LiteralPath $files -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archive + '.sha256', $hash + '  ' + [IO.Path]::GetFileName($archive) + "`n", [Text.UTF8Encoding]::new($false))
Write-Host "Portable archive: $archive"
Write-Host "SHA256: $hash"

if($BuildInstaller) {
    if([string]::IsNullOrWhiteSpace($InstallerOutputDirectory)) {
        $InstallerOutputDirectory = [IO.Path]::GetDirectoryName($archive)
    }
    & (Join-Path $PSScriptRoot 'windows\Build-Installer.ps1') -Payload $stage -Architecture $Architecture -OutputDirectory $InstallerOutputDirectory -IsccPath $IsccPath
}
