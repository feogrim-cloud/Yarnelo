[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Executable,
    [AllowEmptyCollection()]
    [AllowEmptyString()]
    [string[]]$Arguments = @(),
    [string]$WorkingDirectory,
    [string]$ReadyLog,
    [ValidateRange(1,120)]
    [int]$TimeoutSeconds = 15,
    [switch]$PassThru
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Start a user-requested GUI independently of the caller's lifetime. This is
# deliberately separate from Yarnelo's logon registration: no existing task,
# startup preference, credentials, job limits or application data is changed.
# Task Scheduler deletion removes the definition, not the running instance:
# https://learn.microsoft.com/windows-server/administration/windows-commands/schtasks-delete
$sourceMarker = 'Yarnelo.IndependentLaunch.v1'
$taskNamespace = 'http://schemas.microsoft.com/windows/2004/02/mit/task'

function Assert-Condition([bool]$Condition,[string]$Message) {
    if(!$Condition) { throw $Message }
}
function Assert-NoReparse([string]$Path) {
    for($ancestor=$Path; $ancestor; $ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
        if(Test-Path -LiteralPath $ancestor) {
            $item=Get-Item -LiteralPath $ancestor -Force
            Assert-Condition (!(($item.Attributes -band [IO.FileAttributes]::ReparsePoint))) 'Independent launch refuses redirected paths.'
        }
    }
}
function Get-AbsoluteLocalPath([string]$Value,[string]$Label) {
    Assert-Condition (![string]::IsNullOrWhiteSpace($Value) -and $Value -match '^[A-Za-z]:[\\/]' -and $Value.IndexOf('"') -lt 0 -and $Value.IndexOf([char]0) -lt 0) "$Label must be an absolute, unquoted local path."
    $path=[IO.Path]::GetFullPath($Value)
    Assert-NoReparse $path
    return $path
}
function Assert-LiteralTaskValue([string]$Value) {
    # The scheduling service expands these forms itself, even without a shell.
    # Reject them rather than forwarding a different target/argument silently.
    Assert-Condition ($Value.IndexOf([char]0) -lt 0 -and $Value -notmatch '%[^%]+%' -and !$Value.Contains('$(')) 'Task Scheduler expansion references (%NAME% or $(...)) are not supported by independent launch.'
}
function ConvertTo-WindowsArgument([string]$Value) {
    # CommandLineToArgvW / Microsoft CRT quoting. Quote every argument, including
    # an empty one; double backslashes only before a quote or the closing quote.
    $text=New-Object Text.StringBuilder
    [void]$text.Append('"')
    $slashes=0
    foreach($character in $Value.ToCharArray()) {
        if($character -eq [char]92) { $slashes++; continue }
        if($character -eq [char]34) {
            [void]$text.Append(('\' * (2*$slashes+1)))
            [void]$text.Append('"')
        } else {
            if($slashes) { [void]$text.Append(('\' * $slashes)) }
            [void]$text.Append($character)
        }
        $slashes=0
    }
    if($slashes) { [void]$text.Append(('\' * (2*$slashes))) }
    [void]$text.Append('"')
    return $text.ToString()
}
function Get-LeafException($ErrorValue) {
    $failure=$ErrorValue
    if($failure -is [Management.Automation.ErrorRecord]) { $failure=$failure.Exception }
    while($failure.InnerException) { $failure=$failure.InnerException }
    return $failure
}
function Release-Com($Value) {
    if($null -ne $Value -and [Runtime.InteropServices.Marshal]::IsComObject($Value)) {
        try { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($Value) } catch [Runtime.InteropServices.InvalidComObjectException] { }
    }
}
function Find-TemporaryTask {
    try { return $folder.GetTask($taskName) }
    catch {
        $failure=Get-LeafException $_
        if($failure.HResult -eq -2147024894 -or $failure.HResult -eq -2147216625) { return $null }
        throw
    }
}
function Resolve-UserSid([string]$Value) {
    if($Value.StartsWith('S-1-',[StringComparison]::OrdinalIgnoreCase)) {
        return ([Security.Principal.SecurityIdentifier]::new($Value)).Value
    }
    return ([Security.Principal.NTAccount]::new($Value)).Translate([Security.Principal.SecurityIdentifier]).Value
}
function Add-TaskElement($Document,$Parent,[string]$Name,[string]$Value) {
    $element=$Document.CreateElement($Name,$taskNamespace)
    if($null -ne $Value) { $element.InnerText=$Value }
    [void]$Parent.AppendChild($element)
    return $element
}
function New-TemporaryTaskXml {
    $document=New-Object Xml.XmlDocument
    $document.XmlResolver=$null
    $task=$document.CreateElement('Task',$taskNamespace)
    $task.SetAttribute('version','1.2')
    [void]$document.AppendChild($task)
    $registration=Add-TaskElement $document $task 'RegistrationInfo' $null
    [void](Add-TaskElement $document $registration 'Author' $userSid)
    [void](Add-TaskElement $document $registration 'Description' ('One-time independent Yarnelo launch: '+$taskName))
    [void](Add-TaskElement $document $registration 'Source' $sourceMarker)
    [void](Add-TaskElement $document $task 'Triggers' $null)
    $principals=Add-TaskElement $document $task 'Principals' $null
    $principal=Add-TaskElement $document $principals 'Principal' $null
    $principal.SetAttribute('id','Author')
    [void](Add-TaskElement $document $principal 'UserId' $userSid)
    [void](Add-TaskElement $document $principal 'LogonType' 'InteractiveToken')
    [void](Add-TaskElement $document $principal 'RunLevel' 'LeastPrivilege')
    $settings=Add-TaskElement $document $task 'Settings' $null
    $settingsValues=[ordered]@{
        MultipleInstancesPolicy='IgnoreNew'; DisallowStartIfOnBatteries='false'; StopIfGoingOnBatteries='false'
        AllowHardTerminate='false'; StartWhenAvailable='false'; RunOnlyIfNetworkAvailable='false'
    }
    foreach($name in $settingsValues.Keys) { [void](Add-TaskElement $document $settings $name $settingsValues[$name]) }
    $idle=Add-TaskElement $document $settings 'IdleSettings' $null
    [void](Add-TaskElement $document $idle 'StopOnIdleEnd' 'false')
    [void](Add-TaskElement $document $idle 'RestartOnIdle' 'false')
    $settingsValues=[ordered]@{
        AllowStartOnDemand='true'; Enabled='true'; Hidden='false'; RunOnlyIfIdle='false'
        WakeToRun='false'; ExecutionTimeLimit='PT0S'; Priority='7'
    }
    foreach($name in $settingsValues.Keys) { [void](Add-TaskElement $document $settings $name $settingsValues[$name]) }
    $actions=Add-TaskElement $document $task 'Actions' $null
    $actions.SetAttribute('Context','Author')
    $action=Add-TaskElement $document $actions 'Exec' $null
    [void](Add-TaskElement $document $action 'Command' $executablePath)
    [void](Add-TaskElement $document $action 'Arguments' $commandArguments)
    [void](Add-TaskElement $document $action 'WorkingDirectory' $workingPath)
    return $document.OuterXml
}
function Assert-OwnedTask($Task) {
    Assert-Condition ($null -ne $Task) 'The temporary launch task could not be read back.'
    $xml=[string]$Task.Xml
    Assert-Condition ($xml.Length -le 131072) 'The temporary launch task is unexpectedly large.'
    $readerSettings=New-Object Xml.XmlReaderSettings
    $readerSettings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $readerSettings.XmlResolver=$null
    $readerSettings.MaxCharactersInDocument=131072
    $xmlInput=New-Object IO.StringReader($xml)
    $reader=[Xml.XmlReader]::Create($xmlInput,$readerSettings)
    $document=New-Object Xml.XmlDocument
    $document.XmlResolver=$null
    try { $document.Load($reader) } finally { $reader.Dispose(); $xmlInput.Dispose() }
    $names=New-Object Xml.XmlNamespaceManager($document.NameTable)
    $names.AddNamespace('t',$taskNamespace)
    $root='/t:Task/t:'
    function Read-TaskValue([string]$XPath) {
        $node=$document.SelectSingleNode($XPath,$names)
        if($null -eq $node) { return '' }
        return $node.InnerText
    }
    Assert-Condition ((Read-TaskValue ($root+'RegistrationInfo/t:Source')) -ceq $sourceMarker -and (Read-TaskValue ($root+'RegistrationInfo/t:Description')) -ceq ('One-time independent Yarnelo launch: '+$taskName)) 'The temporary task ownership marker changed; it will not be used or removed.'
    Assert-Condition ((Resolve-UserSid (Read-TaskValue ($root+'RegistrationInfo/t:Author'))) -eq $userSid) 'The temporary task author changed.'
    $principals=$document.SelectNodes($root+'Principals/*',$names)
    $actions=$document.SelectNodes($root+'Actions/*',$names)
    $triggers=$document.SelectNodes($root+'Triggers/*',$names)
    Assert-Condition ($principals.Count -eq 1 -and $principals[0].LocalName -eq 'Principal' -and $principals[0].NamespaceURI -eq $taskNamespace -and $actions.Count -eq 1 -and $actions[0].LocalName -eq 'Exec' -and $actions[0].NamespaceURI -eq $taskNamespace -and $triggers.Count -eq 0) 'The temporary launch task has unexpected actions, principals or triggers.'
    # Scheduler omits a serialized RunLevel when it has the schema default.
    # An absent value is LeastPrivilege; an explicit HighestAvailable is not.
    $runLevel=Read-TaskValue ($root+'Principals/t:Principal/t:RunLevel')
    Assert-Condition ((Resolve-UserSid (Read-TaskValue ($root+'Principals/t:Principal/t:UserId'))) -eq $userSid -and (Read-TaskValue ($root+'Principals/t:Principal/t:LogonType')) -ceq 'InteractiveToken' -and ($runLevel -ceq '' -or $runLevel -ceq 'LeastPrivilege') -and (Read-TaskValue ($root+'Principals/t:Principal/t:GroupId')) -eq '') 'The temporary task is not a current-user, least-privilege interactive launch.'
    $principalId=$principals[0].GetAttribute('id')
    Assert-Condition (![string]::IsNullOrEmpty($principalId) -and $document.SelectSingleNode($root+'Actions',$names).GetAttribute('Context') -ceq $principalId) 'The temporary task action has the wrong security context.'
    $actualExecutable=Read-TaskValue ($root+'Actions/t:Exec/t:Command')
    $actualDirectory=Read-TaskValue ($root+'Actions/t:Exec/t:WorkingDirectory')
    Assert-Condition ([string]::Equals($actualExecutable,$executablePath,[StringComparison]::OrdinalIgnoreCase) -and [string]::Equals($actualDirectory,$workingPath,[StringComparison]::OrdinalIgnoreCase) -and (Read-TaskValue ($root+'Actions/t:Exec/t:Arguments')) -ceq $commandArguments) 'The temporary task command, arguments or working directory changed.'
    $required=[ordered]@{
        MultipleInstancesPolicy='IgnoreNew'; DisallowStartIfOnBatteries='false'; StopIfGoingOnBatteries='false'
        AllowHardTerminate='false'; StartWhenAvailable='false'; RunOnlyIfNetworkAvailable='false'
        AllowStartOnDemand='true'; Enabled='true'; Hidden='false'; RunOnlyIfIdle='false'; WakeToRun='false'
        ExecutionTimeLimit='PT0S'; Priority='7'; 'IdleSettings/t:StopOnIdleEnd'='false'; 'IdleSettings/t:RestartOnIdle'='false'
    }
    # The service suppresses default-valued XML elements on readback. Interpret
    # only documented equivalent defaults; battery/termination/time-limit
    # overrides must remain explicit because their defaults are unsuitable.
    # https://learn.microsoft.com/windows/win32/taskschd/taskschedulerschema-settingstype-complextype
    $schemaDefaults=@{
        MultipleInstancesPolicy='IgnoreNew'; StartWhenAvailable='false'; RunOnlyIfNetworkAvailable='false'
        AllowStartOnDemand='true'; Enabled='true'; Hidden='false'; RunOnlyIfIdle='false'; WakeToRun='false'
        Priority='7'; 'IdleSettings/t:RestartOnIdle'='false'
    }
    foreach($name in $required.Keys) {
        $actual=Read-TaskValue ($root+'Settings/t:'+$name)
        if($actual -ceq '' -and $schemaDefaults.ContainsKey($name)) { $actual=$schemaDefaults[$name] }
        Assert-Condition ($actual -ceq $required[$name]) ('The temporary task launch setting changed: '+$name)
    }
    Assert-Condition ([bool]$Task.Enabled) 'The temporary launch task is disabled.'
}
function Get-ExactProcesses {
    foreach($process in [Diagnostics.Process]::GetProcessesByName('Yarnelo')) {
        try {
            if($process.HasExited) { continue }
            try { $path=$process.MainModule.FileName }
            catch {
                if($process.HasExited) { continue }
                throw 'An existing Yarnelo process could not be identified safely. Launch it independently only after closing that instance.'
            }
            if([string]::Equals($path,$executablePath,[StringComparison]::OrdinalIgnoreCase)) {
                [pscustomobject]@{ ProcessId=$process.Id; SessionId=$process.SessionId; StartedUtc=$process.StartTime.ToUniversalTime() }
            }
        } finally { $process.Dispose() }
    }
}
function Read-ReadyLines {
    if([string]::IsNullOrEmpty($readyPath) -or !(Test-Path -LiteralPath $readyPath)) { return @() }
    Assert-NoReparse $readyPath
    Assert-Condition ([IO.File]::Exists($readyPath)) 'The readiness log is not a regular file.'
    $stream=[IO.File]::Open($readyPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        if($stream.Length -gt 131072) { [void]$stream.Seek(-131072,[IO.SeekOrigin]::End) }
        $reader=New-Object IO.StreamReader($stream,[Text.Encoding]::UTF8,$true,4096,$true)
        try { $text=$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
    return @([regex]::Matches($text,'(?m)^[^\r\n]*\bpid=[0-9]+ ready\r?$') | ForEach-Object { $_.Value.TrimEnd([char]13) })
}

Assert-Condition ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) 'Independent launch requires Windows.'
$executablePath=Get-AbsoluteLocalPath $Executable 'Executable'
Assert-Condition ([string]::Equals([IO.Path]::GetFileName($executablePath),'Yarnelo.exe',[StringComparison]::OrdinalIgnoreCase) -and [IO.File]::Exists($executablePath)) 'An existing Yarnelo.exe is required.'
Assert-Condition ([Diagnostics.FileVersionInfo]::GetVersionInfo($executablePath).ProductName -ceq 'Yarnelo') 'The executable product must be Yarnelo.'
if([string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory=[IO.Path]::GetDirectoryName($executablePath) }
$workingPath=Get-AbsoluteLocalPath $WorkingDirectory 'WorkingDirectory'
Assert-Condition ([IO.Directory]::Exists($workingPath)) 'The working directory does not exist.'
$readyPath=''
if(![string]::IsNullOrWhiteSpace($ReadyLog)) {
    $readyPath=Get-AbsoluteLocalPath $ReadyLog 'ReadyLog'
    Assert-Condition ([IO.Directory]::Exists([IO.Path]::GetDirectoryName($readyPath))) 'The readiness log directory does not exist.'
}
Assert-LiteralTaskValue $executablePath
Assert-LiteralTaskValue $workingPath
$quotedArguments=@(foreach($argument in $Arguments) {
    Assert-Condition ($null -ne $argument) 'Null arguments are not supported; use an empty string for an empty argument.'
    Assert-LiteralTaskValue $argument
    ConvertTo-WindowsArgument $argument
})
$commandArguments=$quotedArguments -join ' '
Assert-Condition (($commandArguments.Length+$executablePath.Length+4) -lt 32767) 'The launch command exceeds the Windows command-line limit.'
Assert-Condition (@(Get-ExactProcesses).Count -eq 0) 'This Yarnelo executable is already running. No independent launch was attempted.'
$identity=[Security.Principal.WindowsIdentity]::GetCurrent()
try { Assert-Condition ($null -ne $identity.User) 'The signed-in user could not be identified.'; $userSid=$identity.User.Value }
finally { $identity.Dispose() }
$currentProcess=[Diagnostics.Process]::GetCurrentProcess()
try { $sessionId=$currentProcess.SessionId } finally { $currentProcess.Dispose() }
Assert-Condition ($sessionId -gt 0) 'An interactive desktop session is required.'
$taskName='Yarnelo.Launch.'+$userSid+'.'+[Guid]::NewGuid().ToString('N')
$service=$null; $folder=$null; $registered=$null; $running=$null
$mayOwnTask=$false; $temporaryTaskRemoved=$false; $failure=$null; $launchedProcessId=0
$launchMode='TaskScheduler.RunEx'
$originalReadyLines=@(Read-ReadyLines)
try {
    $service=New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $folder=$service.GetFolder('\')
    $prior=Find-TemporaryTask
    try { Assert-Condition ($null -eq $prior) 'The unique temporary task name already exists; nothing was changed.' } finally { Release-Com $prior }
    $xml=New-TemporaryTaskXml
    $mayOwnTask=$true
    try { $registered=$folder.RegisterTask($taskName,$xml,2,$userSid,$null,3,$null) } # TASK_CREATE only; InteractiveToken
    catch {
        if((Get-LeafException $_).HResult -eq -2147024713) { $mayOwnTask=$false } # ERROR_ALREADY_EXISTS: never remove a preexisting definition.
        throw
    }
    $readback=Find-TemporaryTask
    try { Assert-OwnedTask $readback } finally { Release-Com $readback }
    Assert-NoReparse $executablePath
    Assert-NoReparse $workingPath
    Assert-Condition (@(Get-ExactProcesses).Count -eq 0) 'This Yarnelo executable started elsewhere before the independent launch.'
    $launchStartedUtc=[DateTime]::UtcNow
    try { $running=$registered.RunEx($null,4,$sessionId,$null) } # TASK_RUN_USE_SESSION_ID, no credentials or elevation.
    catch {
        $runFailure=Get-LeafException $_
        if($runFailure -is [MissingMethodException] -or $runFailure.HResult -eq -2147352573 -or $runFailure.HResult -eq -2147352570 -or $runFailure.HResult -eq -2147467263) {
            $launchMode='TaskScheduler.Run'
            $running=$registered.Run($null)
        } else { throw }
    }
    Assert-Condition ($null -ne $running) 'Task Scheduler did not return a launch instance.'
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $firstSeen=$null
    $ready=$false
    while($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        $candidates=@(Get-ExactProcesses)
        Assert-Condition ($candidates.Count -le 1) 'More than one new exact Yarnelo process was found; independent launch could not be verified.'
        if($candidates.Count -eq 1) {
            $candidate=$candidates[0]
            Assert-Condition ($candidate.SessionId -eq $sessionId -and $candidate.StartedUtc -ge $launchStartedUtc.AddSeconds(-2)) 'The new Yarnelo process belongs to an unexpected session or launch time.'
            if($launchedProcessId -eq 0) { $launchedProcessId=$candidate.ProcessId; $firstSeen=$watch.Elapsed.TotalSeconds }
            Assert-Condition ($candidate.ProcessId -eq $launchedProcessId) 'The launched Yarnelo process exited and was replaced before verification.'
            $ready=[string]::IsNullOrEmpty($readyPath)
            if(!$ready) {
                foreach($line in @(Read-ReadyLines)) {
                    if($line -match ('\bpid='+$launchedProcessId+' ready$') -and $originalReadyLines -cnotcontains $line) { $ready=$true; break }
                }
            }
            if($ready -and ($watch.Elapsed.TotalSeconds-$firstSeen) -ge .25) { break }
        } elseif($launchedProcessId -ne 0) { throw 'The launched Yarnelo process exited before verification.' }
        Start-Sleep -Milliseconds 75
    }
    Assert-Condition ($launchedProcessId -ne 0 -and $ready -and ($watch.Elapsed.TotalSeconds-$firstSeen) -ge .25) 'The independent Yarnelo launch did not become ready before the timeout. No process was terminated.'
} catch { $failure=$_ }
finally {
    if($mayOwnTask -and $null -ne $folder) {
        $cleanupTask=$null; $remaining=$null
        try {
            $cleanupTask=Find-TemporaryTask
            if($null -ne $cleanupTask) {
                Assert-OwnedTask $cleanupTask
                # Removing a task never stops its running GUI. Do not call Stop,
                # terminate a process, or retry through a caller-owned child.
                $folder.DeleteTask($taskName,0)
            }
            $remaining=Find-TemporaryTask
            Assert-Condition ($null -eq $remaining) ('The temporary launch task could not be removed: '+$taskName)
            $temporaryTaskRemoved=$true
        } catch {
            $cleanupMessage='Temporary task cleanup could not be verified: '+$taskName+'. '+$_.Exception.Message
            if($null -eq $failure) { $failure=New-Object Management.Automation.ErrorRecord([InvalidOperationException]::new($cleanupMessage),'YarneloIndependentLaunchCleanupFailed',[Management.Automation.ErrorCategory]::InvalidResult,$taskName) }
            else { $failure=New-Object Management.Automation.ErrorRecord([InvalidOperationException]::new($failure.Exception.Message+' '+$cleanupMessage),'YarneloIndependentLaunchFailed',[Management.Automation.ErrorCategory]::InvalidResult,$taskName) }
        } finally { Release-Com $remaining; Release-Com $cleanupTask }
    }
    Release-Com $running; Release-Com $registered; Release-Com $folder; Release-Com $service
}
if($null -ne $failure) { throw $failure }
if($PassThru) {
    [pscustomobject]@{
        ProcessId=$launchedProcessId
        TemporaryTaskRemoved=$temporaryTaskRemoved
        LaunchMode=$launchMode
        TemporaryTaskName=$taskName
    }
}
