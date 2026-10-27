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
    $taskName = 'Yarnelo.Logon.' + [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $taskXml = $null
    if($null -ne $task) { $taskXml = Export-ScheduledTask -TaskName $taskName }
    return ConvertTo-Json -InputObject ([ordered]@{ RunKeys = $snapshots; TaskXml = $taskXml }) -Compress -Depth 5
}

function New-ProfileFixture([string]$Path, [string]$Label) {
    Assert-Condition (!(Test-Path -LiteralPath $Path)) "An existing profile was found: $Path"
    Assert-NoReparsePoints $Path
    $library = Join-Path $Path 'Library'
    New-Item -ItemType Directory -Path $library | Out-Null
    $items = @()
    foreach($index in 1..19) {
        $id = [Guid]::NewGuid().ToString('N')
        $shortcut = Join-Path $library ($id + '.lnk')
        # These files are synthetic and never launched. No desktop source was
        # moved, so uninstall must leave the library/profile bytes untouched.
        [IO.File]::WriteAllText($shortcut, ($Label + ' shortcut ' + $index), [Text.UTF8Encoding]::new($false))
        $items += [ordered]@{ Id = $id; Name = ($Label + ' ' + $index); Path = $shortcut; ManagedShortcut = $true; DesktopSourceMoved = $false; OriginalPath = '' }
    }
    $settings = [ordered]@{ Items = $items; LaunchAtLogin = $false; Theme = 'frost'; Language = 'en'; ciSentinel = [Guid]::NewGuid().ToString('N') }
    [IO.File]::WriteAllText((Join-Path $Path 'settings.json'), (ConvertTo-Json -InputObject $settings -Depth 8), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $Path 'settings.json') -Destination (Join-Path $Path 'settings.json.bak')
    return Get-ProfileHashes $Path
}

function Get-ProfileHashes([string]$Path) {
    $hashes = @{}
    foreach($file in Get-ChildItem -LiteralPath $Path -File -Recurse) {
        $hashes[$file.FullName.Substring($Path.Length + 1)] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
    return $hashes
}

function Write-ShortcutFixture([string]$Path, [string]$Description) {
    Assert-Condition (!(Test-Path -LiteralPath $Path)) "A desktop fixture must never overwrite an existing file: $Path"
    Assert-NoReparsePoints $Path
    if([IO.Path]::GetExtension($Path) -eq '.url') {
        [IO.File]::WriteAllText($Path, ("[InternetShortcut]`r`nURL=https://example.invalid/" + [Uri]::EscapeDataString($Description) + "`r`n"), [Text.UTF8Encoding]::new($false))
    } else {
        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut($Path)
        try {
            $link.TargetPath = Join-Path $env:WINDIR 'System32\notepad.exe'
            $link.Description = $Description
            $link.Arguments = '"' + $Description + '"'
            $link.Save()
        }
        finally {
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
        }
    }
}

function Add-DesktopRestoreFixtures([string]$Profile, [string]$Label) {
    # Only reached after the disposable hosted-runner guard. These must be the
    # actual Windows known folders: the production helper rejects substitutes.
    $userDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $publicDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
    foreach($folder in @($userDesktop, $publicDesktop)) {
        Assert-Condition (Test-Path -LiteralPath $folder -PathType Container) 'A hosted runner desktop folder is missing.'
        Assert-NoReparsePoints $folder
    }
    $prefix = 'Yarnelo CI ' + $Label + ' ' + [Guid]::NewGuid().ToString('N')
    $cases = @(
        [pscustomobject]@{ Name='user'; Extension='.lnk'; Directory=$userDesktop; State='missing' },
        [pscustomobject]@{ Name='public'; Extension='.url'; Directory=$publicDesktop; State='missing' },
        [pscustomobject]@{ Name='conflict'; Extension='.lnk'; Directory=$userDesktop; State='conflict' },
        [pscustomobject]@{ Name='identical'; Extension='.url'; Directory=$userDesktop; State='identical' },
        [pscustomobject]@{ Name='numbered'; Extension='.lnk'; Directory=$publicDesktop; State='numbered' }
    )
    $saved = Get-Content -LiteralPath (Join-Path $Profile 'settings.json') -Raw | ConvertFrom-Json
    $before = @{}
    $expected = @{}
    $missingSource = $null
    foreach($case in $cases) {
        $id = [Guid]::NewGuid().ToString('N')
        $source = Join-Path (Join-Path $Profile 'Library') ($id + $case.Extension)
        $original = Join-Path $case.Directory ($prefix + ' ' + $case.Name + $case.Extension)
        $numbered = Join-Path $case.Directory ($prefix + ' ' + $case.Name + ' (2)' + $case.Extension)
        foreach($candidate in @($original, $numbered)) {
            Assert-Condition (!(Test-Path -LiteralPath $candidate)) 'A generated desktop fixture path is already occupied.'
        }
        Write-ShortcutFixture $source ($prefix + ' preserved ' + $case.Name)
        $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
        $saved.Items += [pscustomobject]@{ Id=$id; Name=$case.Name; Path=$source; OriginalPath=$original; ManagedShortcut=$true; DesktopSourceMoved=$true }
        if($null -eq $missingSource) { $missingSource = $source }
        if($case.State -in @('conflict','numbered')) {
            Write-ShortcutFixture $original ($prefix + ' unrelated occupied ' + $case.Name)
            $before[$original] = (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash
            $expected[$original] = $before[$original]
            $expected[$numbered] = $sourceHash
            if($case.State -eq 'numbered') {
                Copy-Item -LiteralPath $source -Destination $numbered
                $before[$numbered] = $sourceHash
            }
        } else {
            $expected[$original] = $sourceHash
            if($case.State -eq 'identical') {
                Copy-Item -LiteralPath $source -Destination $original
                $before[$original] = $sourceHash
            }
        }
    }
    $saved.LaunchAtLogin = $true
    [IO.File]::WriteAllText((Join-Path $Profile 'settings.json'), (ConvertTo-Json -InputObject $saved -Depth 8), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $Profile 'settings.json') -Destination (Join-Path $Profile 'settings.json.bak') -Force
    return [pscustomobject]@{ Prefix=$prefix; Directories=@($userDesktop,$publicDesktop); Before=$before; Expected=$expected; MissingSource=$missingSource; Profile=$Profile; Hashes=(Get-ProfileHashes $Profile) }
}

function Assert-DesktopRestoreFixtures($Fixture, [bool]$Restored) {
    $hashes = if($Restored) { $Fixture.Expected } else { $Fixture.Before }
    $files = @($Fixture.Directories | ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter ($Fixture.Prefix + '*') -File })
    Assert-Condition ($files.Count -eq $hashes.Count) 'Uninstall created duplicate restored shortcuts or changed an unrelated occupied name.'
    foreach($path in $hashes.Keys) {
        Assert-Condition (Test-Path -LiteralPath $path -PathType Leaf) "A desktop fixture is missing: $path"
        Assert-NoReparsePoints $path
        Assert-Condition ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $hashes[$path]) "Uninstall overwrote or changed desktop shortcut bytes: $path"
    }
}

function New-OwnedStartupFixture([string]$Executable) {
    # Use the real app task name only on this proven-clean disposable runner,
    # without ever starting it. This makes failure-preservation non-vacuous.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $name = 'Yarnelo.Logon.' + $sid
    Assert-Condition ($null -eq (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue)) 'The hosted runner already has a Yarnelo logon task.'
    $service = New-Object -ComObject Schedule.Service
    $folder = $null
    $definition = $null
    try {
        $service.Connect()
        $folder = $service.GetFolder('\')
        $definition = $service.NewTask(0)
        $definition.RegistrationInfo.Source = 'Yarnelo.Startup.v1'
        $definition.RegistrationInfo.Author = $sid
        $definition.Principal.Id = 'Author'
        $definition.Principal.UserId = $sid
        $definition.Principal.LogonType = 3
        $definition.Principal.RunLevel = 0
        $definition.Actions.Context = 'Author'
        $action = $definition.Actions.Create(0)
        $action.Path = $Executable
        $action.Arguments = '--startup'
        $action.WorkingDirectory = Split-Path $Executable -Parent
        $trigger = $definition.Triggers.Create(9)
        $trigger.UserId = $sid
        $trigger.Enabled = $true
        $trigger.Delay = 'PT10S'
        $definition.Settings.Enabled = $true
        $definition.Settings.AllowDemandStart = $true
        $definition.Settings.DisallowStartIfOnBatteries = $false
        $definition.Settings.StopIfGoingOnBatteries = $false
        $definition.Settings.ExecutionTimeLimit = 'PT0S'
        $folder.RegisterTaskDefinition($name, $definition, 2, $sid, $null, 3, $null) | Out-Null
    }
    finally {
        foreach($value in @($definition,$folder,$service)) {
            if($null -ne $value) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($value) }
        }
    }
    Assert-Condition ($null -ne (Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue)) 'The synthetic Yarnelo logon task was not registered.'
}

function Assert-UninstallBlocked([string]$Uninstaller, [string]$Executable, $Fixture, [string]$Run) {
    $critical = @{}
    foreach($path in @($Uninstaller, $Executable, (Join-Path (Split-Path $Executable -Parent) 'Yarnelo.SetupMaintenance.exe'))) {
        $critical[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $startup = Read-StartupState
    $held = Join-Path $Run ('temporarily-held-' + [Guid]::NewGuid().ToString('N') + '.lnk')
    Assert-Condition (!(Test-Path -LiteralPath $held)) 'The temporary missing-library fixture must be new.'
    [IO.File]::Move($Fixture.MissingSource, $held)
    try {
        $missingHashes = Get-ProfileHashes $Fixture.Profile
        $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $Run 'blocked-uninstall.log') + '"'))
        $process = Start-Process -FilePath $Uninstaller -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        try { Assert-Condition ($process.ExitCode -ne 0) 'Uninstall succeeded despite a missing managed shortcut; installed files may have been deleted.' }
        finally { $process.Dispose() }
        foreach($path in $critical.Keys) {
            Assert-Condition (Test-Path -LiteralPath $path -PathType Leaf) "Failed uninstall removed an installed file: $path"
            Assert-Condition ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $critical[$path]) "Failed uninstall changed an installed file: $path"
        }
        Assert-ProfileUnchanged $Fixture.Profile $missingHashes 'Blocked uninstallation'
        Assert-DesktopRestoreFixtures $Fixture $false
        Assert-Condition ((Read-StartupState) -ceq $startup) 'Failed uninstall removed or changed the owned logon task.'
    }
    finally {
        if((Test-Path -LiteralPath $held) -and !(Test-Path -LiteralPath $Fixture.MissingSource)) { [IO.File]::Move($held, $Fixture.MissingSource) }
    }
    Assert-ProfileUnchanged $Fixture.Profile $Fixture.Hashes 'Blocked uninstallation retry preparation'
}

function Assert-ProfileUnchanged([string]$Path, [hashtable]$Hashes, [string]$Operation) {
    Assert-Condition (Test-Path -LiteralPath $Path -PathType Container) "$Operation removed a profile: $Path"
    Assert-NoReparsePoints $Path
    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse)
    Assert-Condition ($files.Count -eq $Hashes.Count) "$Operation changed the number of profile files: $Path"
    foreach($relative in $Hashes.Keys) {
        $file = Join-Path $Path $relative
        Assert-Condition (Test-Path -LiteralPath $file -PathType Leaf) "$Operation removed a profile file: $relative"
        Assert-NoReparsePoints $file
        Assert-Condition ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ceq $Hashes[$relative]) "$Operation changed a profile file: $relative"
    }
}

function Assert-InstallLocation([Microsoft.Win32.RegistryHive]$Hive, [string]$Application) {
    $keyName = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{0FD657E9-E4CC-4BD3-8C71-EE8AA91BA43D}_is1'
    $locations = @()
    $views = @([Microsoft.Win32.RegistryView]::Registry32)
    if([Environment]::Is64BitOperatingSystem) { $views += [Microsoft.Win32.RegistryView]::Registry64 }
    foreach($view in $views) {
        $root = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, $view)
        try {
            $key = $root.OpenSubKey($keyName, $false)
            try { if($null -ne $key) { $locations += [string]$key.GetValue('InstallLocation', '') } }
            finally { if($null -ne $key) { $key.Dispose() } }
        }
        finally { $root.Dispose() }
    }
    # Registry32/64 can be shared for some HKCU keys. Compare unique values.
    $locations = @($locations | Select-Object -Unique)
    if([string]::IsNullOrEmpty($Application)) {
        Assert-Condition ($locations.Count -eq 0) "Setup unexpectedly registered a second install scope in $Hive."
    } else {
        Assert-Condition ($locations.Count -eq 1) "Setup did not register exactly one install location in $Hive."
        Assert-Condition ([string]::Equals($locations[0].TrimEnd('\'), $Application.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) "Setup changed the registered directory in $Hive."
    }
}

function Assert-StartShortcut([string]$Link, [string]$Application) {
    Assert-Condition (Test-Path -LiteralPath $Link -PathType Leaf) 'The Start Menu shortcut was not created.'
    $expectedExecutable = Join-Path $Application 'Yarnelo.exe'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Link)
    try {
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($shortcut.TargetPath), $expectedExecutable, [StringComparison]::OrdinalIgnoreCase)) 'The Start Menu shortcut targets a different installation.'
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($shortcut.WorkingDirectory), $Application, [StringComparison]::OrdinalIgnoreCase)) 'The Start Menu shortcut has the wrong working directory.'
        $iconParts = $shortcut.IconLocation -split ',(?=-?\d+$)', 2
        $iconPath = [Environment]::ExpandEnvironmentVariables($iconParts[0].Trim('"'))
        Assert-Condition ([string]::Equals([IO.Path]::GetFullPath($iconPath), $expectedExecutable, [StringComparison]::OrdinalIgnoreCase)) 'The Start Menu shortcut icon belongs to a different application.'
    }
    finally {
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    }
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
    # The runner guard and absence check above/below prevent these fixtures
    # from ever replacing an installed user's actual profile.
    $nativeProfile = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'PolkaDock'
    $nativeHashes = New-ProfileFixture $nativeProfile 'Native installed profile'
    $report['NativeProfile'] = $nativeProfile
    $report['NativeProfileHashes'] = $nativeHashes
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',('/DIR="' + $application + '"'),('/LOG="' + (Join-Path $run 'install.log') + '"'))
    Invoke-HiddenInstaller $setup $arguments 'Installation'
    Assert-NoDockProcess

    $installedExecutable = Join-Path $application 'Yarnelo.exe'
    $marker = Join-Path $application 'portable.mode'
    Assert-Condition (Test-Path -LiteralPath $installedExecutable -PathType Leaf) 'Setup did not install Yarnelo.exe.'
    Assert-Condition (!(Test-Path -LiteralPath $marker)) 'A standard installation unexpectedly enabled portable mode and switched away from the native profile.'
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Installation'
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

    Assert-StartShortcut $startLink $application
    Assert-InstallLocation LocalMachine $application
    Assert-InstallLocation CurrentUser ''
    Assert-Condition (!(Test-Path -LiteralPath $desktopLink)) 'Silent Setup unexpectedly created an optional desktop shortcut.'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Installation changed the existing autostart preference.'

    # Orphan Data beside a standard installation must neither be imported nor
    # select portable mode. Upgrades must preserve both profiles unchanged.
    $data = Join-Path $application 'Data'
    Assert-Condition (!(Test-Path -LiteralPath $data)) 'Setup or a dock launch unexpectedly created a profile.'
    $fixtures = New-ProfileFixture $data 'Legacy portable profile'
    $report['ProfileHashes'] = $fixtures

    # Omit /DIR deliberately: rerunning Setup must remember the existing
    # installation, including when its path differs from the default.
    $upgradeArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',('/LOG="' + (Join-Path $run 'standard-upgrade.log') + '"'))
    Invoke-HiddenInstaller $setup $upgradeArguments 'Standard upgrade without /DIR'
    Assert-NoDockProcess
    Assert-Condition (Test-Path -LiteralPath $installedExecutable -PathType Leaf) 'Upgrade did not retain the original installation directory.'
    Assert-StartShortcut $startLink $application
    Assert-InstallLocation LocalMachine $application
    Assert-InstallLocation CurrentUser ''
    Assert-Condition (!(Test-Path -LiteralPath $marker)) 'Upgrade enabled portable mode for an installed native profile.'
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Standard upgrade'
    Assert-ProfileUnchanged $data $fixtures 'Standard upgrade with orphan Data'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Standard upgrade changed the autostart preference.'

    # Preserve an explicitly portable existing installation as well. A marker
    # can come from an older Setup or a deliberately unpacked portable build.
    [IO.File]::WriteAllText($marker, 'Existing portable installation', [Text.UTF8Encoding]::new($false))
    $markerHash = (Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash
    $portableUpgradeArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',('/LOG="' + (Join-Path $run 'portable-upgrade.log') + '"'))
    Invoke-HiddenInstaller $setup $portableUpgradeArguments 'Existing portable upgrade without /DIR'
    Assert-NoDockProcess
    Assert-Condition (Test-Path -LiteralPath $marker -PathType Leaf) 'Upgrade removed the existing portable profile selector.'
    Assert-Condition ((Get-FileHash -LiteralPath $marker -Algorithm SHA256).Hash -ceq $markerHash) 'Upgrade replaced the existing portable profile selector.'
    Assert-StartShortcut $startLink $application
    Assert-InstallLocation LocalMachine $application
    Assert-InstallLocation CurrentUser ''
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Portable upgrade'
    Assert-ProfileUnchanged $data $fixtures 'Portable upgrade'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Portable upgrade changed the autostart preference.'

    $portableRestoration = Add-DesktopRestoreFixtures $data 'portable'
    $fixtures = $portableRestoration.Hashes
    $report['ProfileHashes'] = $fixtures
    $report['PortableDesktopRestoration'] = $portableRestoration.Expected
    Assert-DesktopRestoreFixtures $portableRestoration $false
    New-OwnedStartupFixture $installedExecutable
    $uninstaller = Join-Path $application 'Uninstall\unins000.exe'
    Assert-Condition (Test-Path -LiteralPath $uninstaller -PathType Leaf) 'Setup did not create the uninstaller.'
    Assert-NoReparsePoints $uninstaller
    Assert-UninstallBlocked $uninstaller $installedExecutable $portableRestoration $run
    Assert-NoDockProcess
    Assert-StartShortcut $startLink $application
    Assert-InstallLocation LocalMachine $application
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Blocked portable uninstallation'
    $uninstallArgs = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $run 'uninstall.log') + '"'))
    Invoke-HiddenInstaller $uninstaller $uninstallArgs 'Uninstallation'
    Assert-NoDockProcess

    Assert-Condition (!(Test-Path -LiteralPath $installedExecutable)) 'Uninstall left Yarnelo.exe installed.'
    Assert-Condition (!(Test-Path -LiteralPath $startLink)) 'Uninstall left the Start Menu shortcut.'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Uninstall changed an unrelated autostart preference.'
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Uninstallation'
    Assert-ProfileUnchanged $data $fixtures 'Uninstallation'
    Assert-DesktopRestoreFixtures $portableRestoration $true

    # Existing per-user installations must retain their privilege scope and
    # directory when the newly standard installer is run without overrides.
    $perUserApplication = Join-Path $run 'current user app'
    $perUserExecutable = Join-Path $perUserApplication 'Yarnelo.exe'
    $perUserMarker = Join-Path $perUserApplication 'portable.mode'
    $perUserArguments = @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',('/DIR="' + $perUserApplication + '"'),('/LOG="' + (Join-Path $run 'current-user-install.log') + '"'))
    Invoke-HiddenInstaller $setup $perUserArguments 'Per-user installation'
    Assert-NoDockProcess
    Assert-Condition (Test-Path -LiteralPath $perUserExecutable -PathType Leaf) 'The per-user installation was not created.'
    Assert-Condition (!(Test-Path -LiteralPath $perUserMarker)) 'Per-user Setup unexpectedly selected portable mode.'
    Assert-StartShortcut $startLink $perUserApplication
    Assert-InstallLocation CurrentUser $perUserApplication
    Assert-InstallLocation LocalMachine ''
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Per-user installation'
    $perUserUpgradeArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/TASKS=',('/LOG="' + (Join-Path $run 'current-user-upgrade.log') + '"'))
    Invoke-HiddenInstaller $setup $perUserUpgradeArguments 'Per-user upgrade without scope or directory overrides'
    Assert-NoDockProcess
    Assert-Condition (!(Test-Path -LiteralPath $perUserMarker)) 'Per-user upgrade switched the settings profile.'
    Assert-StartShortcut $startLink $perUserApplication
    Assert-InstallLocation CurrentUser $perUserApplication
    Assert-InstallLocation LocalMachine ''
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Per-user upgrade'
    Assert-ProfileUnchanged $data $fixtures 'Per-user upgrade'
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Per-user installation or upgrade changed the autostart preference.'
    $perUserUninstaller = Join-Path $perUserApplication 'Uninstall\unins000.exe'
    Assert-Condition (Test-Path -LiteralPath $perUserUninstaller -PathType Leaf) 'Per-user Setup did not create its uninstaller.'
    Assert-NoReparsePoints $perUserUninstaller
    $nativeRestoration = Add-DesktopRestoreFixtures $nativeProfile 'native'
    $nativeHashes = $nativeRestoration.Hashes
    $report['NativeProfileHashes'] = $nativeHashes
    $report['NativeDesktopRestoration'] = $nativeRestoration.Expected
    Assert-DesktopRestoreFixtures $nativeRestoration $false
    New-OwnedStartupFixture $perUserExecutable
    $perUserUninstallArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $run 'current-user-uninstall.log') + '"'))
    Invoke-HiddenInstaller $perUserUninstaller $perUserUninstallArguments 'Per-user uninstallation'
    Assert-NoDockProcess
    Assert-Condition (!(Test-Path -LiteralPath $perUserExecutable)) 'Per-user uninstall left Yarnelo.exe installed.'
    Assert-Condition (!(Test-Path -LiteralPath $startLink)) 'Per-user uninstall left its Start Menu shortcut.'
    Assert-InstallLocation CurrentUser ''
    Assert-InstallLocation LocalMachine ''
    Assert-ProfileUnchanged $nativeProfile $nativeHashes 'Per-user uninstallation'
    Assert-ProfileUnchanged $data $fixtures 'Per-user uninstallation'
    Assert-DesktopRestoreFixtures $nativeRestoration $true
    Assert-DesktopRestoreFixtures $portableRestoration $true
    Assert-Condition ((Read-StartupState) -ceq $startupBefore) 'Per-user uninstall changed the autostart preference.'
    $report['Result'] = 'Passed'
    Write-Host "PASS: Windows $Architecture Setup installed, upgraded both profile modes and privilege scopes without overrides, refused incomplete restoration, then restored moved user/shared shortcuts before uninstalling; profile bytes, collisions, idempotency, tasks, shortcuts and architecture verified. No dock was launched."
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
