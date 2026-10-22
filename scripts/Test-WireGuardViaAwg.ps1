$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = Join-Path $project 'artifacts\wg-via-awg'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$awg = Join-Path $project 'engines\awg.exe'
$engine = Join-Path $project 'engines\amneziawg.exe'
$sing = Join-Path $project 'engines\sing-box.exe'
$tunnel = 'OneTunnel' + [Guid]::NewGuid().ToString('N').Substring(0,12)
$config = Join-Path $output ($tunnel + '.conf')
$serverFile = Join-Path $output 'server.json'
$server = $null
$result = @{ Success=$false; DefaultRouteChanged=$false; DnsChanged=$false; KillSwitchUsed=$false }
try {
    $clientKey = (& $awg genkey).Trim()
    $clientPublic = ($clientKey | & $awg pubkey).Trim()
    $serverKey = (& $awg genkey).Trim()
    $serverPublic = ($serverKey | & $awg pubkey).Trim()
    $socket = New-Object Net.Sockets.UdpClient(0)
    $port = $socket.Client.LocalEndPoint.Port
    $socket.Dispose()
    @{log=@{level='debug'}; endpoints=@(@{type='wireguard';tag='wg-fixture';system=$false;address=@('10.231.203.1/32');private_key=$serverKey;listen_port=$port;peers=@(@{public_key=$clientPublic;allowed_ips=@('10.231.203.2/32')})});outbounds=@(@{type='direct';tag='direct'})} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $serverFile -Encoding ASCII
    & $sing check -c $serverFile 2>$null
    if($LASTEXITCODE -ne 0){throw 'Fixture config invalid'}
    $server=Start-Process -FilePath $sing -ArgumentList @('run','-c',$serverFile) -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $output 'server-error.txt') -RedirectStandardOutput (Join-Path $output 'server-output.txt')
    $localAddress = [Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() | Where-Object {$_.OperationalStatus -eq 'Up' -and $_.GetIPProperties().GatewayAddresses.Count -gt 0} | ForEach-Object {$_.GetIPProperties().UnicastAddresses} | Where-Object {$_.Address.AddressFamily -eq 'InterNetwork' -and $_.Address.ToString() -notlike '169.254.*'} | Select-Object -First 1
    if(!$localAddress){throw 'No local uplink address for fixture'}
    @"
[Interface]
PrivateKey = $clientKey
Address = 10.231.203.2/32
[Peer]
PublicKey = $serverPublic
Endpoint = $($localAddress.Address):$port
AllowedIPs = 10.231.203.1/32
PersistentKeepalive = 1
"@ | Set-Content -LiteralPath $config -Encoding ASCII
    & $engine /installtunnelservice $config
    if($LASTEXITCODE -ne 0){throw 'AWG tunnel install failed'}
    $handshake = 0L
    for($i=0;$i -lt 30;$i++) {
        Start-Sleep -Milliseconds 500
        $savedPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $text = & $awg show $tunnel latest-handshakes 2>$null
        $queryExit = $LASTEXITCODE
        $ErrorActionPreference = $savedPreference
        if($queryExit -eq 0) {
            foreach($line in $text){$parts=$line -split '\s+';if($parts.Length -ge 2){$handshake=[long]$parts[1]}}
            if($handshake -gt 0){break}
        }
        $serverLog = Get-Content -LiteralPath (Join-Path $output 'server-error.txt') -Raw -ErrorAction SilentlyContinue
        if($serverLog -match 'Receiving keepalive packet|Received keepalive packet'){$handshake=1;break}
    }
    if($handshake -le 0){
        $result.ServiceStatus = [string](Get-Service -Name ('AmneziaWGTunnel$'+$tunnel) -ErrorAction SilentlyContinue).Status
        $result.ServiceQuery = @(& sc.exe query ('AmneziaWGTunnel$'+$tunnel) | ForEach-Object {$_.Replace($tunnel,'fixture')})
        $dump = & $engine /dumplog 2>&1
        $result.Diagnostic = @($dump | Where-Object {$_.Contains('['+$tunnel+']')} | Select-Object -Last 10 | ForEach-Object {$_.Replace($tunnel,'fixture')})
        throw 'No authenticated standard WireGuard handshake through AWG'
    }
    $result.Success=$true
    $result.AuthenticatedWireGuardHandshake=$true
} catch {
    $result.Error=$_.Exception.Message
} finally {
    & $engine /uninstalltunnelservice $tunnel
    for($i=0;$i -lt 30;$i++){if(!(Get-Service -Name ('AmneziaWGTunnel$'+$tunnel) -ErrorAction SilentlyContinue)){break};Start-Sleep -Milliseconds 200}
    if($server -and !$server.HasExited){$server.Kill();$server.WaitForExit()}
    foreach($file in @($config,$serverFile)){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file -Force}}
    $result.Cleaned = !(Get-Service -Name ('AmneziaWGTunnel$'+$tunnel) -ErrorAction SilentlyContinue)
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'result.json')
}
if(!$result.Success -or !$result.Cleaned){exit 1}
