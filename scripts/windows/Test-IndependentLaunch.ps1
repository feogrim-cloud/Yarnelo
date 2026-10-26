[CmdletBinding()]
param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repository=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(!$OutputDirectory) { $OutputDirectory=Join-Path $repository 'work' }
if(![IO.Path]::IsPathRooted($OutputDirectory)) { throw 'An absolute test output directory is required.' }
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
function Assert-NoReparse([string]$Path) {
    for($ancestor=[IO.Path]::GetFullPath($Path);$ancestor;$ancestor=[IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Fixture paths cannot contain reparse points.' }
    }
}
Assert-NoReparse $OutputDirectory
$helper=Join-Path $PSScriptRoot 'Start-Independent.ps1'
if(!(Test-Path -LiteralPath $helper)) { throw 'The independent-launch helper is missing.' }
$token=[Guid]::NewGuid().ToString('N')
$run=Join-Path $OutputDirectory ('yarnelo-independent-test-'+$token)
if(Test-Path -LiteralPath $run) { throw 'The unique fixture directory already exists.' }
New-Item -ItemType Directory -Path $run | Out-Null
Assert-NoReparse $run
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
# A LeastPrivilege task must be able to write even when the automation caller
# has an elevated token. This ACE applies only inside the fresh test directory.
$acl=Get-Acl -LiteralPath $run
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.SecurityIdentifier]::new($sid),
    [Security.AccessControl.FileSystemRights]::Modify,
    ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit),
    [Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
Set-Acl -LiteralPath $run -AclObject $acl
[IO.File]::WriteAllLines((Join-Path $run 'fixture-token.txt'),@($token,$run,$sid),[Text.UTF8Encoding]::new($false))
$executable=Join-Path $run 'Yarnelo.exe'
$harnessExecutable=Join-Path $run 'LifetimeHarness.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ('/out:'+$harnessExecutable) /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll (Join-Path $PSScriptRoot 'IndependentLaunchFixture.cs')
if($LASTEXITCODE -ne 0) { throw 'Independent-launch fixture compilation failed.' }
Copy-Item -LiteralPath $harnessExecutable -Destination $executable
Copy-Item -LiteralPath $harnessExecutable -Destination (Join-Path $run 'DirectControl.exe')
$caller=Join-Path $run 'caller.ps1'
$callerSource=@'
param([string]$FixtureRoot,[string]$Token,[string]$Helper)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
function Quote-ProcessArgument([string]$Value) {
    return '"'+[regex]::Replace($Value,'(\\*)"','$1$1\"').TrimEnd('\')+('\\' * ([regex]::Match($Value,'\\*$').Length))+'"'
}
try {
    $exe=Join-Path $FixtureRoot 'Yarnelo.exe'
    $control=[Diagnostics.ProcessStartInfo]::new()
    $control.FileName=Join-Path $FixtureRoot 'DirectControl.exe'
    $control.UseShellExecute=$false
    $control.CreateNoWindow=$true
    $control.WorkingDirectory=$FixtureRoot
    $control.Arguments=(@('--sentinel',$FixtureRoot,$Token,'direct') | ForEach-Object {Quote-ProcessArgument $_}) -join ' '
    $direct=[Diagnostics.Process]::Start($control)
    $direct.Dispose()
    $literals=@('with spaces','',"`t",'Русский_日本語','say "hello"','C:\ends\','C:\slashes\\\"tail','100% literal')
    $arguments=@('--sentinel',$FixtureRoot,$Token,'scheduled')+$literals
    $result=& $Helper -Executable $exe -Arguments $arguments -WorkingDirectory $FixtureRoot -PassThru
    $document=[Xml.XmlDocument]::new()
    $root=$document.CreateElement('Result')
    [void]$document.AppendChild($root)
    foreach($name in @('ProcessId','TemporaryTaskRemoved','TemporaryTaskName','LaunchMode')) {
        $element=$document.CreateElement($name)
        $element.InnerText=[string]$result.$name
        [void]$root.AppendChild($element)
    }
    $document.Save((Join-Path $FixtureRoot 'caller-result.xml.pending'))
    Move-Item -LiteralPath (Join-Path $FixtureRoot 'caller-result.xml.pending') -Destination (Join-Path $FixtureRoot 'caller-result.xml')
    Start-Sleep -Seconds 40
} catch {
    [IO.File]::WriteAllText((Join-Path $FixtureRoot 'caller-error.txt'),($_ | Out-String),[Text.UTF8Encoding]::new($false))
    exit 91
}
'@
[IO.File]::WriteAllText($caller,$callerSource,[Text.UTF8Encoding]::new($true))
$shell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$report=[ordered]@{Result='Started';Fixture=$run;ProductionProfileAccessed=$false;ExistingJobModified=$false}
try {
    $harnessArguments=@('--harness',$run,$token,$caller,$shell,$helper) | ForEach-Object {
        if($_.Contains('"') -or $_.EndsWith('\')) { throw 'Unexpected quote or trailing separator in fixture harness argument.' }
        '"'+$_+'"'
    }
    $harness=Start-Process -FilePath $harnessExecutable -ArgumentList $harnessArguments -WindowStyle Hidden -PassThru -Wait
    $harnessExit=$harness.ExitCode
    $harness.Dispose()
    if($harnessExit -ne 0) {
        foreach($name in @('caller-error.txt','fixture-error.txt')) {
            $detail=Join-Path $run $name
            if(Test-Path -LiteralPath $detail) { Write-Host ([IO.File]::ReadAllText($detail)) }
        }
        throw 'The isolated independent-launch lifetime test failed.'
    }
    $receipt=[Xml.XmlDocument]::new()
    $receipt.PreserveWhitespace=$true
    $receipt.Load((Join-Path $run 'scheduled-receipt.xml'))
    $actual=@($receipt.SelectNodes('/Receipt/Arguments/Argument') | ForEach-Object {[string]$_.InnerText})
    $expected=@('with spaces','',"`t",'Русский_日本語','say "hello"','C:\ends\','C:\slashes\\\"tail','100% literal')
    if($actual.Count -ne $expected.Count) { throw 'The scheduled fixture received the wrong argument count.' }
    for($index=0;$index -lt $expected.Count;$index++) {
        if($actual[$index] -cne $expected[$index]) { throw ('The scheduled fixture argument was changed at index '+$index) }
    }
    $report['Result']='Passed'
    $report['ArgumentRoundTrip']='Passed'
    $report['LifetimeIsolation']='Passed'
    $report['TemporaryTaskRemoval']='Passed'
    $tokenPolicy=@([IO.File]::ReadAllLines((Join-Path $run 'token-policy.txt')))
    if($tokenPolicy.Count -ne 7 -or $tokenPolicy[0] -cne 'Passed' -or $tokenPolicy[6] -cne 'PolicyAssertions=18') {throw 'The native elevation-policy proof is missing.'}
    $report['TokenPolicy']='Passed'
    $report['TokenPolicyDetails']=$tokenPolicy[1..6]
    Write-Host 'PASS: exact quoted/empty/Unicode arguments, same user/session, no token elevation beyond the caller, temporary task removal and post-parent-job heartbeat verified.'
    Write-Host ($tokenPolicy[1..6] -join '; ')
} catch { $report['Result']='Failed'; $report['Error']=$_.Exception.Message; throw }
finally {
    [IO.File]::WriteAllText((Join-Path $run 'test-result.json'),(ConvertTo-Json -InputObject $report -Depth 4),[Text.UTF8Encoding]::new($false))
    Write-Host ('Independent-launch fixture diagnostics preserved: '+$run)
}
