param([switch]$Elevated, [switch]$Capture, [switch]$KillSwitch, [switch]$Smoke, [switch]$Service, [switch]$FixtureOnly, [switch]$Migration)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$admin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (!$admin) {
    if ($Elevated) { throw 'Windows administrator token was not granted.' }
    $arguments = @('-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-Elevated')
    if ($Capture) { $arguments += '-Capture' }
    if ($KillSwitch) { $arguments += '-KillSwitch' }
    if ($Smoke) { $arguments += '-Smoke' }
    if ($Service) { $arguments += '-Service' }
    if ($FixtureOnly) { $arguments += '-FixtureOnly' }
    if ($Migration) { $arguments += '-Migration' }
    # UAC is shown by Windows. No subscription URL or key is passed on the command line.
    $elevatedRun = Start-Process powershell.exe -Verb RunAs -WindowStyle Hidden -ArgumentList $arguments -Wait -PassThru
    if ($elevatedRun.ExitCode -ne 0) { throw 'Elevated system test failed; inspect its protected report.' }
    return
}
$run = Join-Path $project ('artifacts\system-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$env:ONETUNNEL_SYSTEM_REPORT_DIR = $run
$env:ONETUNNEL_SYSTEM_PROFILES = Join-Path $env:LOCALAPPDATA 'OneTunnel\profiles.bin'
$env:ONETUNNEL_DATA_DIR = Join-Path $run 'private-runtime'
$env:ONETUNNEL_TEST_FIXTURE_ONLY = if ($FixtureOnly) { '1' } else { '0' }
$captureStarted = $false
$watchTask = $null
Push-Location $project
try {
    dotnet build .\tests\OneTunnel.Tests.csproj --nologo *> (Join-Path $run 'build.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed; see build.txt.' }
    # The test runner creates restrictive ACLs before saving network snapshots.
    # Do not capture until that private report directory has been secured.
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in @($identity.User, (New-Object Security.Principal.SecurityIdentifier 'S-1-5-18'), (New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-544'))) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $run -AclObject $acl
    if ($Service -or $Migration) {
        # Scheduler owns the SYSTEM watchdog independently of this PowerShell
        # tree. No system connection is started until its readiness file exists.
        $watchTask = 'OneTunnel-Test-Recovery-' + [guid]::NewGuid().ToString('N')
        $dotnet = (Get-Command dotnet.exe).Source
        $assembly = Join-Path $project 'tests\bin\Debug\net10.0-windows\OneTunnel.Tests.dll'
        $birth = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks
        $watchArgs = '"' + $assembly + '" --service-safety-watch "' + $run + '" ' + $PID + ' ' + $birth
        $action = New-ScheduledTaskAction -Execute $dotnet -Argument $watchArgs
        $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 35) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        Register-ScheduledTask -TaskName $watchTask -Action $action -Principal $principal -Settings $settings | Out-Null
        Start-ScheduledTask -TaskName $watchTask
        for ($i = 0; $i -lt 40 -and !(Test-Path -LiteralPath (Join-Path $run 'watch-ready')); $i++) { Start-Sleep -Milliseconds 250 }
        if (!(Test-Path -LiteralPath (Join-Path $run 'watch-ready'))) { throw 'Independent network recovery watchdog could not start; system test cancelled.' }
    }
    if ($Capture) {
        pktmon filter list *> (Join-Path $run 'capture-filters.txt')
        pktmon list *> (Join-Path $run 'capture-components.txt')
        # An existing capture is never stopped or replaced. Existing filters are
        # retained and must be reviewed: they can make a capture inconclusive.
        pktmon start --capture --comp nics --pkt-size 128 --file-size 32 --file-name (Join-Path $run 'network.etl') *> (Join-Path $run 'capture-start.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Packet capture could not start. Existing tracing was left untouched.' }
        $captureStarted = $true
    }
    $testMode = if($Migration){"--go-migration-compatibility"}elseif($Service){"--service-check"}elseif($Smoke){"--kill-switch-smoke"}elseif($KillSwitch){"--kill-switch-check"}else{"--system-mode"}
    $runner = Start-Process -FilePath (Get-Command dotnet.exe).Source -WindowStyle Hidden -ArgumentList @(('"' + (Join-Path $project 'tests\bin\Debug\net10.0-windows\OneTunnel.Tests.dll') + '"'), $testMode) -RedirectStandardOutput (Join-Path $run 'checks.txt') -RedirectStandardError (Join-Path $run 'errors.txt') -PassThru -Wait
    Set-Content -LiteralPath (Join-Path $run 'runner-exit.txt') -Value $runner.ExitCode
    if ($runner.ExitCode -ne 0) { throw 'System checks incomplete; see errors.txt.' }
} catch {
    $_.Exception.ToString() | Set-Content -LiteralPath (Join-Path $run 'launcher-error.txt')
    throw
} finally {
    if ($watchTask -and !(Test-Path -LiteralPath (Join-Path $run 'watch-armed'))) {
        Set-Content -LiteralPath (Join-Path $run 'watch-finished') -Value 'done'
        Start-Sleep -Seconds 1
        Unregister-ScheduledTask -TaskName $watchTask -Confirm:$false -ErrorAction SilentlyContinue
    }
    if ($captureStarted) {
        pktmon stop *> (Join-Path $run 'capture-stop.txt')
        pktmon etl2pcap (Join-Path $run 'network.etl') --out (Join-Path $run 'network.pcapng') *> (Join-Path $run 'capture-convert.txt')
    }
    Pop-Location
}
