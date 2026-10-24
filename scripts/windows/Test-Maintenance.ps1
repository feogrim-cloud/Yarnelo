[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This runner creates native windows and exercises a real process-close helper.
# Keep it off developer machines and self-hosted runners, even when manually invoked.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
   $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Maintenance tests run only on disposable GitHub-hosted Windows runners.'
}
if([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or ![IO.Path]::IsPathRooted($env:RUNNER_TEMP)) {
    throw 'The GitHub runner temporary directory is missing or invalid.'
}

function Assert-Condition([bool]$Condition, [string]$Message) {
    if(!$Condition) { throw $Message }
}
function Assert-NoReparsePoints([string]$Path) {
    for($ancestor = $Path; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and
           ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "A fixture directory must not contain a reparse point: $ancestor"
        }
    }
}
function Quote-Argument([string]$Argument) {
    if($Argument.Contains('"')) { throw 'Unexpected quote in a generated fixture argument.' }
    return '"' + $Argument + '"'
}

$temporaryRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
Assert-NoReparsePoints $temporaryRoot
$run = Join-Path $temporaryRoot ('yarnelo-maintenance-' + $Architecture + '-' + [Guid]::NewGuid().ToString('N'))
Assert-Condition ($run.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'Fixtures must remain below RUNNER_TEMP.'
Assert-Condition (!(Test-Path -LiteralPath $run)) 'The generated fixture directory already exists.'
New-Item -ItemType Directory -Path $run | Out-Null

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$helper = Join-Path $run 'Yarnelo.SetupMaintenance.exe'
$fixture = Join-Path $run 'FixtureTemplate.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$helper" /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll (Join-Path $PSScriptRoot 'SetupMaintenance.cs') (Join-Path $PSScriptRoot '..\..\src\StartupRegistration.cs')
if($LASTEXITCODE -ne 0) { throw 'Maintenance helper compilation failed.' }
if($Architecture -eq 'arm64') {
    & (Join-Path $PSScriptRoot 'Invoke-Arm64Build.ps1') -Project (Join-Path $PSScriptRoot 'MaintenanceFixture.Arm64.csproj') -OutputDirectory $run
} else {
    & $compiler /nologo /target:winexe "/platform:$Architecture" /optimize+ /codepage:65001 "/out:$fixture" /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'MaintenanceFixture.cs')
    if($LASTEXITCODE -ne 0) { throw 'Synthetic fixture compilation failed.' }
}

$fixtures = [Collections.Generic.List[object]]::new()
function Start-Fixture([string]$Name, [string]$Language) {
    $directory = Join-Path $run $Name
    Assert-Condition (!(Test-Path -LiteralPath $directory)) 'A fixture directory must be new.'
    New-Item -ItemType Directory -Path $directory | Out-Null
    $executable = Join-Path $directory 'Yarnelo.exe'
    Copy-Item -LiteralPath $fixture -Destination $executable
    $ready = Join-Path $directory 'ready.txt'
    $closed = Join-Path $directory 'closed.txt'
    $eventName = 'Local\Yarnelo.MaintenanceFixture.' + [Guid]::NewGuid().ToString('N')
    $cleanup = [Threading.EventWaitHandle]::new($false, [Threading.EventResetMode]::ManualReset, $eventName)
    $arguments = @($Language, (Quote-Argument $ready), (Quote-Argument $closed), (Quote-Argument $eventName))
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $state = [pscustomobject]@{ Process = $process; Cleanup = $cleanup; Executable = $executable; Ready = $ready; Closed = $closed }
    $fixtures.Add($state)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while(!(Test-Path -LiteralPath $ready) -and !$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 50
    }
    Assert-Condition ((Test-Path -LiteralPath $ready) -and !$process.HasExited) "Synthetic $Name window did not become ready."
    Assert-Condition ([IO.File]::ReadAllText($ready).StartsWith($process.Id.ToString() + '|', [StringComparison]::Ordinal)) 'Ready marker must belong to the exact fixture process.'
    return $state
}
function Invoke-Close([string]$Executable) {
    # This executable is always a newly compiled synthetic fixture below this run.
    Assert-Condition ($Executable.StartsWith($run + '\', [StringComparison]::OrdinalIgnoreCase)) 'Refusing to target anything outside the new fixture directory.'
    $process = Start-Process -FilePath $helper -ArgumentList @('close', (Quote-Argument $Executable)) -WindowStyle Hidden -PassThru -Wait
    try { return $process.ExitCode }
    finally { $process.Dispose() }
}

try {
    foreach($language in @('en', 'ru')) {
        $target = Start-Fixture ('target-' + $language) $language
        $decoy = Start-Fixture ('same-name-other-path-' + $language) $language
        Assert-Condition ((Invoke-Close $target.Executable) -eq 0) "The $language fixture could not be closed gracefully."
        Assert-Condition ($target.Process.WaitForExit(5000) -and $target.Process.ExitCode -eq 0) "The $language fixture did not exit after WM_CLOSE."
        Assert-Condition ((Test-Path -LiteralPath $target.Closed) -and [IO.File]::ReadAllText($target.Closed) -eq 'WM_CLOSE') "The $language fixture must record a real WM_CLOSE."
        Assert-Condition (!$decoy.Process.HasExited -and !(Test-Path -LiteralPath $decoy.Closed)) "The $language same-name process at another path must remain untouched."
        $decoy.Cleanup.Set() | Out-Null
        Assert-Condition ($decoy.Process.WaitForExit(5000)) 'The decoy fixture did not stop through its private cleanup event.'
        Write-Host "PASS: $Architecture $language closes by WM_CLOSE; same-name process at another path remains untouched."
    }
    $unrelated = Start-Fixture 'same-path-unrelated-title' 'unrelated'
    Assert-Condition ((Invoke-Close $unrelated.Executable) -eq 50) 'An unrelated window title must be refused without a successful close.'
    Assert-Condition (!$unrelated.Process.HasExited -and !(Test-Path -LiteralPath $unrelated.Closed)) 'The unrelated window at the target path must remain untouched.'
    Write-Host "PASS: $Architecture unrelated title is not closed; exact known title guard is retained."
}
finally {
    # Only unique private events are signalled. Never enumerate, terminate or
    # broadly close Yarnelo processes, including on a failed assertion.
    foreach($state in $fixtures) {
        try {
            if(!$state.Process.HasExited) {
                $state.Cleanup.Set() | Out-Null
                if(!$state.Process.WaitForExit(5000)) { Write-Warning "Synthetic fixture $($state.Process.Id) will stop at its own 120-second deadline." }
            }
        }
        finally { $state.Cleanup.Dispose(); $state.Process.Dispose() }
    }
}
Write-Host "Maintenance integration tests passed. Isolated fixtures: $run"
