# ==============================================================
#  Computer Info Collector
#  Collects hardware / OS / software info and reports it to the
#  server, then opens the registration page in the default browser.
#
#  Usage:
#    .\collect.ps1                                # resolve server URL (see below)
#    .\collect.ps1 -ServerUrl http://192.168.1.100:3000
#    .\collect.ps1 -NoOpen                        # do not open browser
#    .\collect.ps1 -NoPause                       # do not wait at the end
#
#  Server URL resolution (priority order):
#    1) -ServerUrl parameter
#    2) server.txt next to this script (first non-empty, non-# line)
#    3) First run will prompt and save to server.txt
#
#  Requires: Windows PowerShell 5.1+ (no admin rights needed)
# ==============================================================
param(
    [string]$ServerUrl = "",
    [switch]$NoOpen,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'

# Script directory (PSScriptRoot is empty when run via cmd /c, so add fallback)
$ScriptDir = $PSScriptRoot
if (-not $ScriptDir) { $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $ScriptDir) { $ScriptDir = (Get-Location).Path }

# Resolve server URL: 1) parameter > 2) server.txt > 3) interactive prompt (saved)
if (-not $ServerUrl) {
    $cfgFile = Join-Path $ScriptDir 'server.txt'
    if (Test-Path $cfgFile) {
        $line = Get-Content $cfgFile -ErrorAction SilentlyContinue |
            Where-Object { $_ -and -not $_.TrimStart().StartsWith('#') } |
            Select-Object -First 1
        if ($line) { $ServerUrl = $line.Trim() }
    }
}

if (-not $ServerUrl) {
    Write-Host "Server address is not configured." -ForegroundColor Yellow
    Write-Host "Enter the server URL (e.g. http://192.168.1.100  or  http://your-server:3000)"
    Write-Host "This will be saved to server.txt so you do not need to type it again."
    $ServerUrl = Read-Host "> "
    if ($ServerUrl -and -not $ServerUrl.StartsWith('http://') -and -not $ServerUrl.StartsWith('https://')) {
        $ServerUrl = 'http://' + $ServerUrl
    }
    if ($ServerUrl) {
        try {
            $cfgFile = Join-Path $ScriptDir 'server.txt'
            "$ServerUrl`r`n# You can edit this line to change the server address." | Out-File -FilePath $cfgFile -Encoding UTF8 -ErrorAction Stop
            Write-Host "Saved to $cfgFile" -ForegroundColor Green
        } catch { }
        Write-Host ""
    }
}

$ServerUrl = $ServerUrl.TrimEnd('/')

if (-not $ServerUrl) {
    Write-Host "FAILED: No server address provided." -ForegroundColor Red
    Write-Host "Usage:"
    Write-Host "  .\collect.ps1 -ServerUrl http://your-server"
    Write-Host "  Or place the URL in a server.txt file next to collect.ps1."
    if (-not $NoPause) { Read-Host "Press Enter to exit" }
    exit 1
}

Write-Host "==============================================" -ForegroundColor Cyan
Write-Host " Computer Info Collector" -ForegroundColor Cyan
Write-Host "==============================================" -ForegroundColor Cyan

try {
    Write-Host "[1/4] Collecting system information..."

    # --- Core system info ---
    $cs   = Get-CimInstance -ClassName Win32_ComputerSystem
    $os   = Get-CimInstance -ClassName Win32_OperatingSystem
    $bios = Get-CimInstance -ClassName Win32_BIOS
    $cpu  = Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1

    $hostname = $env:COMPUTERNAME
    $brand    = if ($cs.Manufacturer) { $cs.Manufacturer.Trim() } else { '' }
    $model    = if ($cs.Model)        { $cs.Model.Trim() }        else { '' }
    $serial   = if ($bios.SerialNumber) { $bios.SerialNumber.Trim() } else { '' }

    # --- Device type: Laptop or Desktop ---
    $deviceType = 'Desktop'
    try {
        $pcSystemType = $cs.PCSystemType   # 1 = Desktop, 2 = Mobile
        $chassis = (Get-CimInstance -ClassName Win32_SystemEnclosure).ChassisTypes
        $laptopTypes  = @(8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32)
        $desktopTypes = @(3, 4, 5, 6, 7, 13, 15, 16, 17, 23, 24)

        if ($pcSystemType -eq 2) {
            $deviceType = 'Laptop'
        } elseif ($pcSystemType -eq 1) {
            if ($chassis -and ($chassis | Where-Object { $laptopTypes -contains $_ })) {
                $deviceType = 'Laptop'
            }
        } elseif ($chassis -and ($chassis | Where-Object { $laptopTypes -contains $_ })) {
            $deviceType = 'Laptop'
        } elseif ($chassis -and ($chassis | Where-Object { $desktopTypes -contains $_ })) {
            $deviceType = 'Desktop'
        }
    } catch { }

    # --- Logged on user ---
    $loggedUser = $cs.UserName
    if (-not $loggedUser) { $loggedUser = "$env:USERDOMAIN\$env:USERNAME" }

    # --- Memory ---
    $memSticks = @(Get-CimInstance -ClassName Win32_PhysicalMemory)
    $memoryTotalGB = 0
    if ($memSticks.Count -gt 0) {
        $memoryTotalGB = [math]::Round((($memSticks | Measure-Object -Property Capacity -Sum).Sum) / 1GB, 2)
    }
    $memory = @($memSticks | ForEach-Object {
        $speed = $_.ConfiguredClockSpeed
        if (-not $speed) { $speed = $_.Speed }
        [ordered]@{
            slot         = [string]$_.DeviceLocator
            manufacturer = ([string]$_.Manufacturer).Trim()
            partno       = ([string]$_.PartNumber).Trim()
            capacity_gb  = [math]::Round($_.Capacity / 1GB, 0)
            speed        = [string]$speed
        }
    })

    # --- Disks (physical drives) ---
    $disks = @(Get-CimInstance -ClassName Win32_DiskDrive | ForEach-Object {
        $sizeGB = $null
        if ($_.Size) { $sizeGB = [math]::Round($_.Size / 1GB, 0) }
        [ordered]@{
            model     = ([string]$_.Model).Trim()
            interface = [string]$_.InterfaceType
            size_gb   = $sizeGB
            serial    = ([string]$_.SerialNumber).Trim()
        }
    })

    # --- Network adapters (enabled only) ---
    $network = @(Get-CimInstance -ClassName Win32_NetworkAdapterConfiguration -Filter "IPEnabled = TRUE" | ForEach-Object {
        $ipv4 = @($_.IPAddress | Where-Object { $_ -match '^\d{1,3}(\.\d{1,3}){3}$' })
        [ordered]@{
            name = ([string]$_.Description).Trim()
            mac  = [string]$_.MACAddress
            ip   = ($ipv4 -join ', ')
        }
    })

    # --- Installed software (registry uninstall entries) ---
    Write-Host "[2/4] Collecting installed software list..."
    $sw = @(Get-ItemProperty -Path `
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -and $_.SystemComponent -ne 1 -and $_.DisplayName -notlike 'KB*' } |
        Select-Object @{ n = 'name'; e = { [string]$_.DisplayName } },
                       @{ n = 'version'; e = { [string]$_.DisplayVersion } },
                       @{ n = 'publisher'; e = { [string]$_.Publisher } } |
        Sort-Object name, version -Unique)

    Write-Host ("[3/4] Found: {0} memory stick(s), {1} disk(s), {2} network adapter(s), {3} software item(s)." -f $memory.Count, $disks.Count, $network.Count, $sw.Count)

    # --- Build payload ---
    $payload = [ordered]@{
        hostname        = $hostname
        brand           = $brand
        model           = $model
        device_type     = $deviceType
        serial          = $serial
        os_version      = [string]$os.Caption
        os_build        = "$($os.Version) (Build $($os.BuildNumber))"
        cpu             = ([string]$cpu.Name).Trim()
        logged_user     = [string]$loggedUser
        memory_total_gb = $memoryTotalGB
        memory          = $memory
        disks           = $disks
        network         = $network
        software        = $sw
    }

    $json = $payload | ConvertTo-Json -Depth 5
    $body = [System.Text.Encoding]::UTF8.GetBytes($json)

    Write-Host "[4/4] Reporting to $ServerUrl ..."
    $resp = Invoke-RestMethod -Uri "$ServerUrl/api/report" -Method Post `
        -ContentType 'application/json; charset=utf-8' -Body $body -TimeoutSec 30

    if ($resp.ok -and $resp.id) {
        $url = "$ServerUrl/?id=$($resp.id)"
        Write-Host ""
        Write-Host "SUCCESS! Record id: $($resp.id)" -ForegroundColor Green
        Write-Host "Opening registration page: $url" -ForegroundColor Green
        if (-not $NoOpen) { Start-Process $url }
    } else {
        throw "Server returned an unexpected response."
    }
}
catch {
    Write-Host ""
    Write-Host "FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Please check: 1) server address is correct  2) server is running  3) network/firewall allows this connection." -ForegroundColor Yellow
    if (-not $NoPause) { Read-Host "Press Enter to exit" }
    exit 1
}

if (-not $NoPause) { Read-Host "Press Enter to exit" }
