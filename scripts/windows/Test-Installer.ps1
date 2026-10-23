[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SetupPath,
    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This test intentionally installs and uninstalls software. Never run it on a
# developer's PC, a self-hosted runner, or against an existing installation.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
   $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Installer tests run only on disposable GitHub-hosted Windows runners.'
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
            throw "A test directory must not contain a reparse point: $ancestor"
        }
    }
}

function Assert-NoDockProcess {
    $running = @(Get-Process -Name Yarnelo -ErrorAction SilentlyContinue)
    Assert-Condition ($running.Count -eq 0) 'A Yarnelo dock process is running; the installer test must never launch the dock.'
}

function Get-PeMachine([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-Condition ($bytes.Length -ge 64) 'The installed executable has no PE header.'
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    Assert-Condition ($offset -ge 0 -and $offset + 26 -le $bytes.Length) 'The installed executable has an invalid PE offset.'
    Assert-Condition ([BitConverter]::ToUInt32($bytes, $offset) -eq 0x00004550) 'The installed executable has an invalid PE signature.'
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}

function Read-StartupState {
    $snapshots = @()
    $views = @([Microsoft.Win32.RegistryView]::Registry32)
    if([Environment]::Is64BitOperatingSystem) { $views += [Microsoft.Win32.RegistryView]::Registry64 }
    foreach($view in $views) {
        $userKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            $runKey = $userKey.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $false)
            try {
                foreach($name in @('Yarnelo', 'Polka Desktop Dock')) {
                    $present = $null -ne $runKey -and @($runKey.GetValueNames()) -contains $name
                    $value = $null
                    $kind = $null
                    if($present) {
                        $value = $runKey.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                        $kind = $runKey.GetValueKind($name).ToString()
                    }
                    $snapshots += [ordered]@{ View = $view.ToString(); Name = $name; Present = $present; Kind = $kind; Value = $value }
                }
            }
            finally { if($null -ne $runKey) { $runKey.Dispose() } }
        }
        finally { $userKey.Dispose() }
    }
    return ConvertTo-Json -InputObject $snapshots -Compress -Depth 5
}

function Invoke-HiddenInstaller([string]$Executable, [string[]]$Arguments, [string]$Operation) {
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -PassThru -Wait
    try {
        Assert-Condition ($process.ExitCode -eq 0) "$Operation failed with exit code $($process.ExitCode). See the preserved test logs."
    }
    finally { $process.Dispose() }
}

$setup = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SetupPath)
Assert-Condition (Test-Path -LiteralPath $setup -PathType Leaf) "The setup executable does not exist: $setup"
Assert-NoReparsePoints $setup
Assert-NoDockProcess

$repository = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$programSource = [IO.File]::ReadAllText((Join-Path $repository 'src\Program.cs'))
$versionMatch = [regex]::Match($programSource, 'AssemblyFileVersion\("([0-9.]+)"\)')
Assert-Condition $versionMatch.Success 'The application version could not be read from Program.cs.'
$expectedVersion = [Version]$versionMatch.Groups[1].Value
$setupVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($setup).FileVersion
Assert-Condition ($setupVersion -eq $expectedVersion) "Setup version $setupVersion does not match source version $expectedVersion."

$programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$startLink = Join-Path $programs 'Yarnelo.lnk'
$desktopLink = Join-Path $desktop 'Yarnelo.lnk'
Assert-Condition (!(Test-Path -LiteralPath $startLink)) 'An existing Yarnelo Start Menu shortcut was found; this runner is not clean.'
Assert-Condition (!(Test-Path -LiteralPath $desktopLink)) 'An existing Yarnelo desktop shortcut was found; this runner is not clean.'
$startupBefore = Read-StartupState

$temporaryRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
Assert-NoReparsePoints $temporaryRoot
$run = Join-Path $temporaryRoot ('yarnelo-installer-' + $Architecture + '-' + [Guid]::NewGuid().ToString('N'))
$application = Join-Path $run 'app'
Assert-Condition ($run.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'The test directory must remain below RUNNER_TEMP.'
Assert-Condition (!(Test-Path -LiteralPath $run)) 'The isolated installer directory already exists.'
New-Item -ItemType Directory -Path $run | Out-Null
Write-Host "Installer test diagnostics are preserved at: $run"

$report = [ordered]@{ Architecture = $Architecture; Version = $expectedVersion.ToString(); Setup = $setup; Directory = $run; Result = 'Started' }
$reportFile = Join-Path $run 'test-result.json'
try {
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',('/DIR="' + $application + '"'),('/LOG="' + (Join-Path $run 'install.log') + '"'))
    Invoke-HiddenInstaller $setup $arguments 'Installation'
    Assert-NoDockProcess

    $installedExecutable = Join-Path $application 'Yarnelo.exe'
    $marker = Join-Path $application 'portable.mode'
    Assert-Condition (Test-Path -LiteralPath $installedExecutable -PathType Leaf) 'Setup did not install Yarnelo.exe.'
    Assert-Condition (Test-Path -LiteralPath $marker -PathType Leaf) 'Setup did not install portable.mode.'
    Assert-NoReparsePoints $installedExecutable
    $expectedMachine = 0x8664
    if($Architecture -eq 'x86') { $expectedMachine = 0x014c }
    if($Architecture -eq 'arm64') { $expectedMachine = 0xaa64 }
    $machine = Get-PeMachine $installedExecutable
    Assert-Condition ($machine -eq $expectedMachine) ('Installed PE machine 0x{0:x4} does not match {1}.' -f $machine, $Architecture)
    $configuration = [IO.File]::ReadAllText((Join-Path $application 'Yarnelo.exe.config'))
    if($Architecture -eq 'arm64') {
        Assert-Condition ($configuration.Contains('Version=v4.8.1')) 'The ARM64 payload must request Framework 4.8.1, which supplies the native ARM64 CLR.'
    }
    $installedVersion = [Version][Diagnostics.FileVersionInfo]::GetVersionInfo($installedExecutable).FileVersion
    Assert-Condition ($installedVersion -eq $setupVersion) "Installed version $installedVersion does not match Setup version $setupVersion."

    Assert-Condition (Test-Path -LiteralPath $startLink -PathType Leaf) 'The Start Menu shortcut was not created.'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($startLink)
    try {
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($shortcut.TargetPath), $installedExecutable, [StringComparison]::OrdinalIgnoreCase)) 'The Start Menu shortcut targets a different installation.'
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($shortcut.WorkingDirectory), $application, [StringComparison]::OrdinalIgnoreCase)) 'The Start Menu shortcut has the wrong working directory.'
        $iconParts = $shortcut.IconLocation -split ',(?=-?\d+$)', 2
        $iconPath = [Environment]::ExpandEnvironmentVariables($iconParts[0].Trim('"'))
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($iconPath), $installedExecutable, [StringComparison]::OrdinalIgnoreCase)) 'The Start Menu shortcut icon belongs to a different application.'
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
    Assert-Condition (!(Test-Path -LiteralPath $desktopLink)) 'Silent Setup unexpectedly created an optional desktop shortcut.'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Installation changed the existing autostart preference.'

    # Synthetic fixtures are deliberately unrelated to any real dock profile.
    $data = Join-Path $application 'Data'
    Assert-Condition (!(Test-Path -LiteralPath $data)) 'Setup or a dock launch unexpectedly created a profile.'
    $library = Join-Path $data 'Library'
    New-Item -ItemType Directory -Path $library | Out-Null
    $sentinel = [Guid]::NewGuid().ToString('N')
    [IO.File]::WriteAllText((Join-Path $data 'settings.json'), ('{"ciSentinel":"' + $sentinel + '","Items":[]}'), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $library 'ci-fixture.txt'), ('Synthetic installer preservation fixture: ' + $sentinel), [Text.UTF8Encoding]::new($false))
    $fixtures = @{}
    foreach($file in Get-ChildItem -LiteralPath $data -File -Recurse) {
        $relative = $file.FullName.Substring($data.Length + 1)
        $fixtures[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    $report['ProfileHashes'] = $fixtures

    $uninstaller = Join-Path $application 'Uninstall\unins000.exe'
    Assert-Condition (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'Setup did not create the uninstaller.'
    Assert-NoReparsePoints $uninstaller
    $uninstallArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $run 'uninstall.log') + '"'))
    Invoke-HiddenInstaller $uninstaller $uninstallArgs 'Uninstallation'
    Assert-NoDockProcess

    Assert-Condition (!(Test-Path -LiteralPath $installedExecutable)) 'Uninstall left Yarnelo.exe installed.'
    Assert-Condition (!(Test-Path -LiteralPath $marker)) 'Uninstall left the installed portable.mode file.'
    Assert-Condition (!(Test-Path -LiteralPath $startLink)) 'Uninstall left the Start Menu shortcut.'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Uninstall changed an unrelated autostart preference.'
    Assert-Condition (Test-Path -LiteralPath $data -PathType Container) 'Uninstall removed the Data directory.'
    $remaining = @(Get-ChildItem -LiteralPath $data -File -Recurse)
    Assert-Condition ($remaining.Count -eq $fixtures.Count) 'Uninstall changed the number of profile fixture files.'
    foreach($relative in $fixtures.Keys) {
        $path = Join-Path $data $relative
        Assert-Condition (Test-Path -LiteralPath $path -PathType Leaf) "Uninstall removed a profile fixture: $relative"
        Assert-Condition ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $fixtures[$relative]) "Uninstall changed a profile fixture: $relative"
    }
    $report['Result'] = 'Passed'
    Write-Host "PASS: Windows $Architecture Setup installed and uninstalled; shortcuts, version, PE architecture and Data preservation verified. No dock was launched."
}
catch {
    $report['Result'] = 'Failed'
    $report['Error'] = $_.Exception.Message
    throw
}
finally {
    [IO.File]::WriteAllText($reportFile, (ConvertTo-Json -InputObject $report -Depth 8), [Text.UTF8Encoding]::new($false))
    # Preserve logs and synthetic Data for inspection. Never recursively remove
    # a directory here, including after a failed or partial installer run.
    Write-Host "Preserved installer test logs and fixtures: $run"
}
