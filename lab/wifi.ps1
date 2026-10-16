param([ValidateSet('status', 'off', 'on')][string]$Action = 'status')

# Turns the Wi-Fi radio on or off with the Windows Radio API (the same switch as airplane mode for Wi-Fi).
# No administrator rights needed. Prints one line: wifi=on|off|none|denied
$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    function Await($op, $type) {
        $t = $asTask.MakeGenericMethod($type).Invoke($null, @($op))
        $t.Wait(-1) | Out-Null
        $t.Result
    }
    [void][Windows.Devices.Radios.Radio, Windows.System.Devices, ContentType = WindowsRuntime]
    [void][Windows.Devices.Radios.RadioAccessStatus, Windows.System.Devices, ContentType = WindowsRuntime]
    [void][Windows.Devices.Radios.RadioState, Windows.System.Devices, ContentType = WindowsRuntime]

    $access = Await ([Windows.Devices.Radios.Radio]::RequestAccessAsync()) ([Windows.Devices.Radios.RadioAccessStatus])
    if ($access -ne 'Allowed') { Write-Output 'wifi=denied'; exit 2 }
    $radios = Await ([Windows.Devices.Radios.Radio]::GetRadiosAsync()) ([System.Collections.Generic.IReadOnlyList[Windows.Devices.Radios.Radio]])
    $wifi = $radios | Where-Object { $_.Kind -eq 'WiFi' } | Select-Object -First 1
    if (-not $wifi) { Write-Output 'wifi=none'; exit 3 }

    if ($Action -eq 'off') { [void](Await ($wifi.SetStateAsync('Off')) ([Windows.Devices.Radios.RadioAccessStatus])) }
    if ($Action -eq 'on') { [void](Await ($wifi.SetStateAsync('On')) ([Windows.Devices.Radios.RadioAccessStatus])) }

    $state = if ($wifi.State -eq 'On') { 'on' } else { 'off' }
    Write-Output "wifi=$state"
}
catch {
    Write-Output "wifi=error $($_.Exception.Message)"
    exit 1
}
