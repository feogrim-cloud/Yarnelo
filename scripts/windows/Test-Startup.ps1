[CmdletBinding()]
param(
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# This checks real Windows task registration and execution only on disposable
# hosted runners. It never uses a production task name, registry value or data.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
   $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Startup integration tests run only on disposable GitHub-hosted Windows runners.'
}
if([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or ![IO.Path]::IsPathRooted($env:RUNNER_TEMP)) {
    throw 'A valid hosted runner temporary directory is required.'
}
function Assert-Condition([bool]$Condition, [string]$Message) { if(!$Condition) { throw $Message } }
function Assert-NoReparsePoints([string]$Path) {
    for($ancestor=$Path; $ancestor; $ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Fixture paths cannot contain a reparse point: $ancestor"
        }
    }
}
function Quote-Argument([string]$Argument) {
    if($Argument.Contains('"')) { throw 'Unexpected quote in a generated fixture argument.' }
    return '"'+$Argument+'"'
}
function Resolve-AccountSid([string]$Account) {
    if($Account.StartsWith('S-1-', [StringComparison]::OrdinalIgnoreCase)) { return $Account }
    return ([Security.Principal.NTAccount]::new($Account)).Translate([Security.Principal.SecurityIdentifier]).Value
}

$repository=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$temporaryRoot=[IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
Assert-NoReparsePoints $temporaryRoot
$token=[Guid]::NewGuid().ToString('N')
$run=Join-Path $temporaryRoot ('yarnelo-startup-'+$Architecture+'-'+$token)
Assert-Condition ($run.StartsWith($temporaryRoot+'\', [StringComparison]::OrdinalIgnoreCase)) 'Fixtures must stay within RUNNER_TEMP.'
Assert-Condition (!(Test-Path -LiteralPath $run)) 'A new isolated fixture directory is required.'
$application=Join-Path $run 'application with spaces'
$otherApplication=Join-Path $run 'different installation'
New-Item -ItemType Directory -Path $run | Out-Null
$executable=Join-Path $application 'Yarnelo.exe'
$otherExecutable=Join-Path $otherApplication 'Yarnelo.exe'
$driver=Join-Path $run 'StartupDriver.exe'
$taskName='Yarnelo.Startup.Test.'+$token
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
# Hosted runners can create temporary files using an elevated runner token.
# Grant this same user access explicitly within the new fixture so a correctly
# limited scheduled token can write its receipt without an Administrators ACE.
$fixtureAcl=Get-Acl -LiteralPath $run
$fixtureAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.SecurityIdentifier]::new($sid),
    [Security.AccessControl.FileSystemRights]::Modify,
    ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,
    [Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $run -AclObject $fixtureAcl
New-Item -ItemType Directory -Path $application,$otherApplication | Out-Null
$report=[ordered]@{ Architecture=$Architecture; Task=$taskName; Executable=$executable; Result='Started' }
$service=$null
$folder=$null
$taskCreated=$false

function Invoke-Driver([string]$Verb, [bool]$ExpectSuccess=$true) {
    $output=Join-Path $run ('driver-'+$Verb+'-'+[Guid]::NewGuid().ToString('N')+'.txt')
    $errors=$output+'.errors'
    $process=Start-Process -FilePath $driver -ArgumentList @($Verb,(Quote-Argument $executable),(Quote-Argument $taskName)) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $output -RedirectStandardError $errors
    try {
        if($ExpectSuccess) { Assert-Condition ($process.ExitCode -eq 0) ("Startup driver $Verb failed with exit $($process.ExitCode): "+[IO.File]::ReadAllText($errors)) }
        else { Assert-Condition ($process.ExitCode -ne 0) "Startup driver $Verb unexpectedly accepted a foreign task." }
        return [IO.File]::ReadAllText($output).Trim()
    }
    finally { $process.Dispose() }
}
function Get-TestTask {
    try { return $folder.GetTask($taskName) }
    catch {
        $failure=$_.Exception
        while($failure.InnerException) { $failure=$failure.InnerException }
        if($failure.HResult -eq -2147024894 -or $failure.HResult -eq -2147216625) { return $null }
        throw
    }
}
function Assert-TaskDefinition($Task) {
    $definition=$Task.Definition
    Assert-Condition ($definition.RegistrationInfo.Source -eq 'Yarnelo.Startup.v1') 'The startup ownership marker is missing.'
    Assert-Condition ((Resolve-AccountSid $definition.Principal.UserId) -eq $sid) 'The startup task is not owned by the current user.'
    Assert-Condition ($definition.Principal.LogonType -eq 3 -and $definition.Principal.RunLevel -eq 0) 'Startup must use InteractiveToken and LeastPrivilege.'
    Assert-Condition ($definition.Actions.Count -eq 1) 'Startup must have exactly one action.'
    $action=$definition.Actions.Item(1)
    Assert-Condition ($action.Type -eq 0 -and [string]::Equals($action.Path,$executable,[StringComparison]::OrdinalIgnoreCase)) 'The task does not target the exact fixture executable.'
    Assert-Condition ($action.Arguments -eq '--startup') 'The startup launch source argument was not retained.'
    Assert-Condition ([string]::Equals($action.WorkingDirectory,$application,[StringComparison]::OrdinalIgnoreCase)) 'Startup has the wrong working directory.'
    Assert-Condition ($definition.Triggers.Count -eq 1) 'Startup must have exactly one logon trigger.'
    $trigger=$definition.Triggers.Item(1)
    Assert-Condition ($trigger.Type -eq 9 -and $trigger.Enabled -and $trigger.Delay -eq 'PT10S') 'The delayed logon trigger is incorrect.'
    Assert-Condition ((Resolve-AccountSid $trigger.UserId) -eq $sid) 'The logon trigger belongs to another user.'
    Assert-Condition (!$definition.Settings.DisallowStartIfOnBatteries -and !$definition.Settings.StopIfGoingOnBatteries) 'Startup must work on battery power.'
    Assert-Condition ($definition.Settings.ExecutionTimeLimit -eq 'PT0S') 'The long-running dock must not have a time limit.'
    Assert-Condition ($definition.Settings.MultipleInstances -eq 2) 'Startup should ignore a new instance while already running.'
    Assert-Condition ($definition.Settings.StartWhenAvailable -and $definition.Settings.AllowDemandStart) 'Startup must allow a delayed start and a manual execution check.'
}

try {
    # AnyCPU is sufficient for this COM fixture, including ARM64 hosted Windows.
    # The separate native ARM64 runtime suite exercises the actual WPF binary.
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    $compilerArgs=@('/nologo','/target:exe','/platform:anycpu','/optimize+','/codepage:65001',('/out:'+$driver),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Xml.dll')
    & $compiler @compilerArgs (Join-Path $repository 'src\StartupRegistration.cs') (Join-Path $PSScriptRoot 'StartupFixture.cs')
    if($LASTEXITCODE -ne 0) { throw 'Startup integration fixture compilation failed.' }
    Copy-Item -LiteralPath $driver -Destination $executable
    Copy-Item -LiteralPath $driver -Destination $otherExecutable
    [IO.File]::WriteAllLines((Join-Path $application 'fixture-token.txt'),@($token,$run,$sid),[Text.UTF8Encoding]::new($false))
    $service=New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $folder=$service.GetFolder('\')
    Assert-Condition ($null -eq (Get-TestTask)) 'The unique test task unexpectedly exists.'

    Assert-Condition ((Invoke-Driver 'status') -eq 'disabled') 'An absent startup task must report disabled.'
    $taskCreated=$true
    Assert-Condition ((Invoke-Driver 'enable') -eq 'enabled') 'Startup registration did not report enabled.'
    $task=Get-TestTask
    Assert-TaskDefinition $task
    $normalXml=$task.Xml
    $task.Enabled=$false
    Assert-Condition ((Invoke-Driver 'status') -eq 'disabled') 'A task disabled in Windows must report disabled in the app.'
    Assert-Condition (!(Get-TestTask).Enabled) 'Reading startup status must not re-enable the task.'
    Assert-Condition ((Invoke-Driver 'enable') -eq 'enabled') 'Explicit enable did not restore the disabled own task.'
    $task=Get-TestTask
    Assert-TaskDefinition $task

    $receipt=Join-Path $application 'startup-receipt.txt'
    $running=$task.Run($null)
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 100
        $task=Get-TestTask
    } while((!(Test-Path -LiteralPath $receipt) -or $task.State -in @(2,4)) -and [DateTime]::UtcNow -lt $deadline)
    Assert-Condition (Test-Path -LiteralPath $receipt) 'Task Scheduler did not start the registered executable.'
    $lines=[IO.File]::ReadAllLines($receipt)
    Assert-Condition ($lines.Length -eq 6 -and $lines[0] -eq $token -and $lines[1] -eq $executable -and $lines[2] -eq $application -and $lines[3] -eq $sid -and $lines[5] -eq '--startup') 'The task launched the wrong path, arguments, directory or user.'
    Assert-Condition ($task.State -notin @(2,4) -and $task.LastTaskResult -eq 0) ('The startup task did not exit successfully: '+$task.LastTaskResult)
    $report['ActualScheduledLaunch']='Passed'

    Assert-Condition ((Invoke-Driver 'disable') -eq 'disabled') 'Disabling startup did not report disabled.'
    Assert-Condition ($null -eq (Get-TestTask)) 'Disabling startup must remove the owned task.'
    $taskCreated=$false

    # The task name is ours for this run, but its action now represents another
    # installation. The backend must neither overwrite nor delete that task.
    [xml]$foreignXml=$normalXml
    $namespace=[Xml.XmlNamespaceManager]::new($foreignXml.NameTable)
    $namespace.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
    $foreignXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:Command',$namespace).InnerText=$otherExecutable
    $foreignXml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:WorkingDirectory',$namespace).InnerText=$otherApplication
    $task=$folder.RegisterTask($taskName,$foreignXml.OuterXml,6,$sid,$null,3,$null)
    $taskCreated=$true
    $foreignBefore=$task.Xml
    [void](Invoke-Driver 'enable' $false)
    [void](Invoke-Driver 'disable')
    Assert-Condition ((Get-TestTask).Xml -ceq $foreignBefore) 'A task targeting a different installation was changed.'

    # Matching executable alone is also insufficient without our source marker.
    [xml]$foreignXml=$normalXml
    $namespace=[Xml.XmlNamespaceManager]::new($foreignXml.NameTable)
    $namespace.AddNamespace('t','http://schemas.microsoft.com/windows/2004/02/mit/task')
    $foreignXml.SelectSingleNode('/t:Task/t:RegistrationInfo/t:Source',$namespace).InnerText='Unrelated isolated test application'
    $task=$folder.RegisterTask($taskName,$foreignXml.OuterXml,6,$sid,$null,3,$null)
    $foreignBefore=$task.Xml
    [void](Invoke-Driver 'enable' $false)
    [void](Invoke-Driver 'disable')
    Assert-Condition ((Get-TestTask).Xml -ceq $foreignBefore) 'A task without the ownership marker was changed.'
    $report['OwnershipChecks']='Passed'
    $report['Result']='Passed'
    Write-Host "PASS: $Architecture startup task uses the current user and normal rights; real scheduled launch, disabled-state detection, removal and foreign-task preservation verified."
}
catch {
    $report['Result']='Failed'
    $report['Error']=$_.Exception.Message
    throw
}
finally {
    $cleanupError=$null
    try {
        if($folder -and $taskCreated) {
            $remaining=Get-TestTask
            if($remaining) {
                $definition=$remaining.Definition
                $action=$definition.Actions.Item(1)
                # Only a unique task created in this run and targeting one of
                # these two isolated fixture directories may be cleaned up.
                Assert-Condition ($remaining.Name -eq $taskName -and $definition.Actions.Count -eq 1 -and
                    (Resolve-AccountSid $definition.Principal.UserId) -eq $sid -and
                    ($action.Path -eq $executable -or $action.Path -eq $otherExecutable)) 'Refusing to clean up an unexpected task.'
                $folder.DeleteTask($taskName,0)
            }
        }
    }
    catch {
        $cleanupError=$_.Exception.Message
        $report['CleanupError']=$cleanupError
        $report['Result']='Failed'
    }
    finally {
        [IO.File]::WriteAllText((Join-Path $run 'test-result.json'),(ConvertTo-Json -InputObject $report -Depth 5),[Text.UTF8Encoding]::new($false))
        if($folder) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($folder) }
        if($service) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($service) }
        Write-Host "Startup integration diagnostics preserved: $run"
    }
    if($cleanupError) {
        if(!$report.Contains('Error')) { throw $cleanupError }
        Write-Warning ('Cleanup failed after the original test failure: '+$cleanupError)
    }
}
