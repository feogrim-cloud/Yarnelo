[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Payload,
    [ValidateSet('x64', 'x86')]
    [string]$Architecture = 'x64',
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,
    [string]$IsccPath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$payloadPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Payload)
$outputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$setupName = 'Yarnelo-Windows-' + $Architecture + '-Setup.exe'
$setupPath = Join-Path $outputPath $setupName
if(Test-Path -LiteralPath $setupPath) { throw "The installer already exists: $setupPath" }
foreach($candidate in @($payloadPath, $outputPath)) {
    for($ancestor = $candidate; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "An installer directory must not be inside a reparse point: $ancestor"
        }
    }
}
$allowed = '^(Yarnelo\.exe|Yarnelo\.exe\.config|Yarnelo\.ico|Yarnelo-\d+\.\d+\.\d+\.ico|portable\.mode|Прочитай меня\.txt|Yarnelo-Icon(?:-(?:16|24|32|48|256))?\.png|Yarnelo-Icon\.svg|LICENSE|README\.md|INSTALL-WINDOWS\.md)$'
foreach($entry in Get-ChildItem -LiteralPath $payloadPath -Force) {
    if($entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $entry.Name -notmatch $allowed) {
        throw "Unexpected file in installer payload: $($entry.Name)"
    }
}
foreach($name in @('Yarnelo.exe','Yarnelo.exe.config','Yarnelo.ico','portable.mode','LICENSE','README.md','INSTALL-WINDOWS.md','Прочитай меня.txt')) {
    if(!(Test-Path -LiteralPath (Join-Path $payloadPath $name) -PathType Leaf)) { throw "Missing installer file: $name" }
}
# AssemblyName.ProcessorArchitecture returns None under modern PowerShell.
# Read the executable's native PE header instead, independent of the shell runtime.
$peBytes = [IO.File]::ReadAllBytes((Join-Path $payloadPath 'Yarnelo.exe'))
$peOffset = [BitConverter]::ToInt32($peBytes, 0x3c)
if($peOffset -lt 0 -or $peOffset + 26 -gt $peBytes.Length -or [BitConverter]::ToUInt32($peBytes, $peOffset) -ne 0x00004550) {
    throw 'The payload does not contain a valid PE executable.'
}
$machine = [BitConverter]::ToUInt16($peBytes, $peOffset + 4)
$expectedMachine = 0x8664
if($Architecture -eq 'x86') { $expectedMachine = 0x014c }
if($machine -ne $expectedMachine) { throw ('Payload machine 0x{0:x4} does not match {1}' -f $machine, $Architecture) }

if([string]::IsNullOrWhiteSpace($IsccPath)) {
    $installed = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if($installed) { $IsccPath = $installed.Source }
    else {
        foreach($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if($base -and (Test-Path -LiteralPath (Join-Path $base 'Inno Setup 6\ISCC.exe') -PathType Leaf)) {
                $IsccPath = Join-Path $base 'Inno Setup 6\ISCC.exe'; break
            }
        }
    }
}
if(!$IsccPath -or !(Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Install Inno Setup 6.3+ from https://jrsoftware.org/isdl.php or pass -IsccPath to its official compiler.'
}
# Yarnelo.iss checks the compiler's own preprocessor version. ISCC.exe's
# Windows file-version resource may legitimately be 0.0.0.0.

# Use the x86 Framework compiler so packaging also works on 32-bit Windows.
# AnyCPU lets this maintenance helper identify native processes on either OS.
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
$maintenancePath = Join-Path ([IO.Path]::GetDirectoryName($payloadPath)) ('Yarnelo.SetupMaintenance-' + [Guid]::NewGuid().ToString('N') + '.exe')
$compilerArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/codepage:65001',('/out:'+$maintenancePath),'/reference:System.dll','/reference:System.Core.dll')
& (Join-Path $framework 'csc.exe') @compilerArgs (Join-Path $PSScriptRoot 'SetupMaintenance.cs')
if($LASTEXITCODE -ne 0) { throw 'Installer maintenance helper compilation failed.' }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$innoArgs = @(('/DPayload='+$payloadPath),('/DOutputDirectory='+$outputPath),('/DAppArchitecture='+$Architecture),('/DMaintenance='+$maintenancePath),(Join-Path $PSScriptRoot 'Yarnelo.iss'))
& $IsccPath @innoArgs
if($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setupPath -PathType Leaf)) { throw 'Inno Setup compilation failed.' }
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($setupPath+'.sha256', $hash+'  '+$setupName+"`n", [Text.UTF8Encoding]::new($false))
Write-Host "Installer: $setupPath"
Write-Host "SHA256: $hash"
