# Egress Connections: shows every remote connection opened by the demo processes.
# Independent of the app. Run it in a third window. Loopback = green, anything else = red.
$names = @('LeakHunt.App', 'Inference.Service.Agent', 'foundry')
Write-Host "EGRESS CONNECTIONS  (processes: $($names -join ', '))" -ForegroundColor Cyan
while ($true) {
    $pids = Get-Process -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.ProcessName } | Select-Object -ExpandProperty Id
    $conns = if ($pids) { Get-NetTCPConnection -State Established -ErrorAction SilentlyContinue | Where-Object { $pids -contains $_.OwningProcess } } else { @() }
    $external = @($conns | Where-Object { $_.RemoteAddress -notin @('127.0.0.1', '::1') })
    $stamp = Get-Date -Format 'HH:mm:ss'
    if ($external.Count -gt 0) {
        foreach ($c in $external) {
            $p = (Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue).ProcessName
            Write-Host "$stamp  EXTERNAL  $p  ->  $($c.RemoteAddress):$($c.RemotePort)" -ForegroundColor Red
        }
    } else {
        Write-Host "$stamp  external connections: 0   (loopback only: $(@($conns).Count))" -ForegroundColor Green
    }
    Start-Sleep -Seconds 2
}
