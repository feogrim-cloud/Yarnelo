[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PortableArchive
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# A real WPF dock is launched only on a disposable native ARM64 hosted runner.
# No installed copy or existing profile is opened, and nothing is force-killed.
if($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_OS -ne 'Windows' -or
   $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_ARCH -ne 'ARM64') {
    throw 'The native ARM64 runtime smoke test requires a disposable GitHub-hosted Windows ARM64 runner.'
}
if([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or ![IO.Path]::IsPathRooted($env:RUNNER_TEMP)) {
    throw 'A valid hosted runner temporary directory is required.'
}
function Assert-Condition([bool]$Condition, [string]$Message) { if(!$Condition) { throw $Message } }
function Assert-NoReparsePoints([string]$Path) {
    for($ancestor = $Path; $ancestor; $ancestor = [IO.Path]::GetDirectoryName($ancestor)) {
        if((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "A runtime test directory must not contain a reparse point: $ancestor"
        }
    }
}
function Get-PeMachine([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    Assert-Condition ($bytes.Length -ge 64) 'The executable has no PE header.'
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    Assert-Condition ($offset -ge 0 -and $offset + 26 -le $bytes.Length) 'The executable has an invalid PE offset.'
    Assert-Condition ([BitConverter]::ToUInt32($bytes, $offset) -eq 0x00004550) 'The executable has an invalid PE signature.'
    return [BitConverter]::ToUInt16($bytes, $offset + 4)
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class YarneloArm64RuntimeNative {
    public delegate bool WindowCallback(IntPtr window, IntPtr argument);
    [DllImport("kernel32.dll", SetLastError=true)] public static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("user32.dll")] public static extern bool EnumWindows(WindowCallback callback, IntPtr argument);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
'@

$archive = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PortableArchive)
Assert-Condition (Test-Path -LiteralPath $archive -PathType Leaf) 'The ARM64 portable archive was not found.'
$temporaryRoot = [IO.Path]::GetFullPath($env:RUNNER_TEMP).TrimEnd('\')
Assert-NoReparsePoints $temporaryRoot
$run = Join-Path $temporaryRoot ('yarnelo-arm64-runtime-' + [Guid]::NewGuid().ToString('N'))
Assert-Condition ($run.StartsWith($temporaryRoot + '\', [StringComparison]::OrdinalIgnoreCase)) 'The runtime fixture must stay within RUNNER_TEMP.'
Assert-Condition (!(Test-Path -LiteralPath $run)) 'The runtime test requires a fresh directory.'
$payload = Join-Path $run 'payload'
$profile = Join-Path $run 'profile'
New-Item -ItemType Directory -Path $payload,$profile -Force | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath $payload
$executable = Join-Path $payload 'Yarnelo.exe'
Assert-Condition ((Get-PeMachine $executable) -eq 0xaa64) 'The dock executable must contain the native ARM64 PE machine, not x64 or AnyCPU.'

# A pre-existing synthetic profile disables first-run shell imports, desktop
# shortcut movement, and startup. It contains no file references to user data.
$settings = '{"MoveDesktopShortcuts":false,"LaunchAtLogin":false,"Items":[],"Rows":2,"Language":"en","AlwaysOnTop":false,"Opacity":0.72,"IconSize":52,"Zoom":1.28}'
[IO.File]::WriteAllText((Join-Path $profile 'settings.json'), $settings, [Text.UTF8Encoding]::new($false))
$report = [ordered]@{ Result='Started'; Executable=$executable; Profile=$profile; PeMachine='0xaa64' }
$process = $null
$script:runtimeWindow = [IntPtr]::Zero
try {
    $process = Start-Process -FilePath $executable -ArgumentList @('--data-dir',('"' + $profile + '"')) -WorkingDirectory $payload -WindowStyle Hidden -PassThru
    $targetId = [uint32]$process.Id
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while(!$process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        [YarneloArm64RuntimeNative]::EnumWindows({
            param([IntPtr]$window, [IntPtr]$unused)
            $owner = [uint32]0
            [void][YarneloArm64RuntimeNative]::GetWindowThreadProcessId($window, [ref]$owner)
            if($owner -eq $targetId) {
                $caption = [Text.StringBuilder]::new(256)
                $className = [Text.StringBuilder]::new(256)
                [void][YarneloArm64RuntimeNative]::GetWindowText($window,$caption,$caption.Capacity)
                [void][YarneloArm64RuntimeNative]::GetClassName($window,$className,$className.Capacity)
                if($caption.ToString() -eq 'Yarnelo — shortcut dock' -and $className.ToString().StartsWith('HwndWrapper[')) {
                    $script:runtimeWindow = $window
                    return $false
                }
            }
            return $true
        },[IntPtr]::Zero) | Out-Null
        if($script:runtimeWindow -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    Assert-Condition (!$process.HasExited -and $script:runtimeWindow -ne [IntPtr]::Zero) 'The real ARM64 dock did not create its WPF window.'
    $processMachine = [uint16]0
    $nativeMachine = [uint16]0
    Assert-Condition ([YarneloArm64RuntimeNative]::IsWow64Process2($process.Handle,[ref]$processMachine,[ref]$nativeMachine)) 'IsWow64Process2 failed for the actual dock process.'
    Assert-Condition ($nativeMachine -eq 0xaa64 -and $processMachine -eq 0) ('The dock must execute natively: process machine=0x{0:x4}, OS machine=0x{1:x4}.' -f $processMachine,$nativeMachine)
    $report['ProcessMachine'] = ('0x{0:x4}' -f $processMachine)
    $report['NativeMachine'] = ('0x{0:x4}' -f $nativeMachine)
    $report['ProcessId'] = $process.Id
    $report['WindowClass'] = 'HwndWrapper'
    $clr = @($process.Modules | Where-Object { $_.ModuleName -ieq 'clr.dll' })
    Assert-Condition ($clr.Count -eq 1) 'The native .NET Framework CLR was not loaded.'
    Assert-Condition ((Get-PeMachine $clr[0].FileName) -eq 0xaa64) 'The loaded .NET Framework CLR must itself be ARM64.'
    $report['ClrPath'] = $clr[0].FileName
    Assert-Condition (!(Test-Path -LiteralPath (Join-Path $profile 'error.log'))) 'The ARM64 dock reported an exception in its isolated profile.'
    $report['Result'] = 'Passed'
}
catch {
    $report['Result'] = 'Failed'
    $report['Error'] = $_.Exception.Message
    throw
}
finally {
    if($null -ne $process) {
        try {
            if(!$process.HasExited -and $script:runtimeWindow -ne [IntPtr]::Zero) {
                $ignored = [IntPtr]::Zero
                [void][YarneloArm64RuntimeNative]::SendMessageTimeout($script:runtimeWindow,0x0010,[IntPtr]::Zero,[IntPtr]::Zero,0x0002,3000,[ref]$ignored)
                Assert-Condition ($process.WaitForExit(15000) -and $process.ExitCode -eq 0) 'The isolated dock did not close gracefully.'
            }
        }
        finally { $process.Dispose() }
    }
    [IO.File]::WriteAllText((Join-Path $run 'test-result.json'),(ConvertTo-Json -InputObject $report -Depth 5),[Text.UTF8Encoding]::new($false))
    Write-Host "ARM64 runtime diagnostics preserved: $run"
}
Write-Host 'PASS: The real ARM64 WPF dock launched with an isolated profile; native ARM64 PE, process and CLR verified; graceful close completed.'
