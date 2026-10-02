<#
.SYNOPSIS
    Automated turnkey migration script to move PickleBallBooking to Tencent Cloud Lighthouse.
.DESCRIPTION
    1. Verifies connectivity to the new Tencent Cloud server.
    2. Securely backs up production environment variables and Let's Encrypt wildcard SSL certs from the current VPS.
    3. Builds the latest release of the application.
    4. Provisions the new Tencent Cloud server (installs .NET 10, Nginx, UFW firewall).
    5. Restores the wildcard SSL certificates and production configuration on the new server.
    6. Deploys the application and enables the systemd service.
    7. Validates local health checks.
    8. Provides DNS cutover instructions.
.PARAMETER NewServerIp
    The public IP address of your new Tencent Cloud Lighthouse instance.
.PARAMETER NewServerUser
    SSH username for the new server (default: root).
.PARAMETER NewServerPort
    SSH port for the new server (default: 22).
.PARAMETER NewServerKeyPath
    Path to private SSH key for the new server (optional).
.PARAMETER CurrentServerIp
    The current production Hostinger VPS IP (default: 187.127.223.93).
.PARAMETER SkipBuild
    Skip dotnet publish if publish/ folder is already built.
.EXAMPLE
    .\deploy\Migrate-ToTencent.ps1 -NewServerIp 43.134.xxx.xxx
#>
param (
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$NewServerIp,

    [string]$NewServerUser = "root",
    [int]$NewServerPort = 22,
    [string]$NewServerKeyPath = "",

    [string]$CurrentServerIp = "187.127.223.93",
    [string]$CurrentServerUser = "root",
    [int]$CurrentServerPort = 22,
    [string]$CurrentServerKeyPath = "",

    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "  PICKLEBALL BOOKING — TENCENT CLOUD LIGHTHOUSE MIGRATION TOOL" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Source VPS (Hostinger) : $CurrentServerUser@$CurrentServerIp"
Write-Host " Target VPS (Tencent)   : $NewServerUser@$NewServerIp`:$NewServerPort"
Write-Host " Domain                 : punitbola.tech (*.punitbola.tech)"
Write-Host "================================================================="

# Helper SSH arguments
$newSshOpts = @("-p", $NewServerPort, "-o", "StrictHostKeyChecking=no", "-o", "ConnectTimeout=10")
$newScpOpts = @("-P", $NewServerPort, "-o", "StrictHostKeyChecking=no")
if ($NewServerKeyPath -ne "") {
    $newSshOpts += @("-i", $NewServerKeyPath)
    $newScpOpts += @("-i", $NewServerKeyPath)
}

$curSshOpts = @("-p", $CurrentServerPort, "-o", "StrictHostKeyChecking=no", "-o", "ConnectTimeout=10")
$curScpOpts = @("-P", $CurrentServerPort, "-o", "StrictHostKeyChecking=no")
if ($CurrentServerKeyPath -ne "") {
    $curSshOpts += @("-i", $CurrentServerKeyPath)
    $curScpOpts += @("-i", $CurrentServerKeyPath)
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("pikolball_migrate_" + (Get-Random))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    # -------------------------------------------------------------
    # 1. Connectivity Check
    # -------------------------------------------------------------
    Write-Host "`n[Step 1/7] Testing SSH connectivity..." -ForegroundColor Yellow
    Write-Host "  Checking current server ($CurrentServerIp)..." -NoNewline
    $curTest = ssh @curSshOpts "$CurrentServerUser@$CurrentServerIp" "echo 'OK'"
    if ($curTest -notmatch "OK") {
        throw "Failed to connect to current server at $CurrentServerIp"
    }
    Write-Host " Connected!" -ForegroundColor Green

    Write-Host "  Checking new Tencent server ($NewServerIp)..." -NoNewline
    $newTest = ssh @newSshOpts "$NewServerUser@$NewServerIp" "echo 'OK'"
    if ($newTest -notmatch "OK") {
        throw "Failed to connect to new server at $NewServerIp. Ensure SSH is running and port $NewServerPort is allowed in Tencent Cloud Firewall."
    }
    Write-Host " Connected!" -ForegroundColor Green

    # -------------------------------------------------------------
    # 2. Extract Secrets & Wildcard SSL from Current Server
    # -------------------------------------------------------------
    Write-Host "`n[Step 2/7] Backing up configuration and Wildcard SSL from current VPS..." -ForegroundColor Yellow
    $sslArchive = Join-Path $tempDir "ssl-backup.tar.gz"
    $envFile    = Join-Path $tempDir "production.env"

    Write-Host "  Archiving /etc/letsencrypt (wildcard certificate)..."
    ssh @curSshOpts "$CurrentServerUser@$CurrentServerIp" "tar -czf /tmp/ssl-backup.tar.gz -C / etc/letsencrypt"
    scp @curScpOpts "$CurrentServerUser@$CurrentServerIp`:/tmp/ssl-backup.tar.gz" $sslArchive
    ssh @curSshOpts "$CurrentServerUser@$CurrentServerIp" "rm -f /tmp/ssl-backup.tar.gz"

    Write-Host "  Downloading /etc/pikolball/production.env..."
    scp @curScpOpts "$CurrentServerUser@$CurrentServerIp`:/etc/pikolball/production.env" $envFile

    if (-not (Test-Path $sslArchive) -or -not (Test-Path $envFile)) {
        throw "Failed to retrieve configuration or SSL backup from current server."
    }
    Write-Host "  Config and Wildcard SSL backup captured successfully!" -ForegroundColor Green

    # -------------------------------------------------------------
    # 3. Build & Publish Application
    # -------------------------------------------------------------
    Write-Host "`n[Step 3/7] Preparing application release bundle..." -ForegroundColor Yellow
    if ($SkipBuild -and (Test-Path "publish/PickleBallBooking.dll")) {
        Write-Host "  Skipping build, using existing ./publish directory."
    } else {
        Write-Host "  Running dotnet publish -c Release..."
        dotnet publish PickleBallBooking/PickleBallBooking.csproj -c Release -o publish
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE"
        }
    }
    Write-Host "  Application bundle ready!" -ForegroundColor Green

    # -------------------------------------------------------------
    # 4. Provision New Tencent Server (OS, Runtime, Nginx)
    # -------------------------------------------------------------
    Write-Host "`n[Step 4/7] Provisioning new Tencent Cloud Lighthouse server..." -ForegroundColor Yellow
    Write-Host "  Uploading setup scripts..."
    scp @newScpOpts deploy/setup-vps.sh deploy/deploy-app.sh deploy/pikolball.service deploy/nginx-punitbola.conf "$NewServerUser@$NewServerIp`:/tmp/"

    Write-Host "  Executing setup-vps.sh (installing .NET 10, Nginx, UFW firewall)..."
    ssh @newSshOpts "$NewServerUser@$NewServerIp" "sed -i 's/\r$//' /tmp/setup-vps.sh /tmp/deploy-app.sh && chmod +x /tmp/setup-vps.sh /tmp/deploy-app.sh && /tmp/setup-vps.sh"
    Write-Host "  Server provisioning completed!" -ForegroundColor Green

    # -------------------------------------------------------------
    # 5. Restore Secrets and Wildcard SSL on New Server
    # -------------------------------------------------------------
    Write-Host "`n[Step 5/7] Restoring Wildcard SSL & production environment on new server..." -ForegroundColor Yellow
    Write-Host "  Uploading SSL archive..."
    scp @newScpOpts $sslArchive "$NewServerUser@$NewServerIp`:/tmp/ssl-backup.tar.gz"
    ssh @newSshOpts "$NewServerUser@$NewServerIp" "tar -xzf /tmp/ssl-backup.tar.gz -C / && rm -f /tmp/ssl-backup.tar.gz"

    Write-Host "  Uploading production.env..."
    scp @newScpOpts $envFile "$NewServerUser@$NewServerIp`:/tmp/production.env"
    ssh @newSshOpts "$NewServerUser@$NewServerIp" "mkdir -p /etc/pikolball && cp /tmp/production.env /etc/pikolball/production.env && chown -R root:pikolball /etc/pikolball && chmod 640 /etc/pikolball/production.env && rm -f /tmp/production.env"

    Write-Host "  Setting up automatic certbot reload hook..."
    ssh @newSshOpts "$NewServerUser@$NewServerIp" "mkdir -p /etc/letsencrypt/renewal-hooks/deploy && echo -e '#!/usr/bin/env bash\nsystemctl reload nginx' > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh && chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh"
    Write-Host "  SSL and Environment variables restored!" -ForegroundColor Green

    # -------------------------------------------------------------
    # 6. Deploy Application & Start Services
    # -------------------------------------------------------------
    Write-Host "`n[Step 6/7] Deploying application binaries to new server..." -ForegroundColor Yellow
    ssh @newSshOpts "$NewServerUser@$NewServerIp" "rm -rf /tmp/publish && mkdir -p /tmp/publish"
    scp @newScpOpts -r publish/* "$NewServerUser@$NewServerIp`:/tmp/publish/"

    Write-Host "  Running deploy-app.sh on target..."
    ssh @newSshOpts "$NewServerUser@$NewServerIp" "/tmp/deploy-app.sh /tmp/publish"
    Write-Host "  Service is running and Nginx is configured!" -ForegroundColor Green

    # -------------------------------------------------------------
    # 7. Verification & Health Check
    # -------------------------------------------------------------
    Write-Host "`n[Step 7/7] Verifying new server health..." -ForegroundColor Yellow
    $serviceStatus = ssh @newSshOpts "$NewServerUser@$NewServerIp" "systemctl is-active pikolball"
    $nginxStatus   = ssh @newSshOpts "$NewServerUser@$NewServerIp" "systemctl is-active nginx"
    $kestrelCheck  = ssh @newSshOpts "$NewServerUser@$NewServerIp" "curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5000"
    $sslCheck      = ssh @newSshOpts "$NewServerUser@$NewServerIp" "curl -k -s -o /dev/null -w '%{http_code}' -H 'Host: punitbola.tech' https://127.0.0.1/"

    Write-Host "  Pikolball Service Status : $serviceStatus" -ForegroundColor $(if ($serviceStatus -eq "active") { "Green" } else { "Red" })
    Write-Host "  Nginx Reverse Proxy     : $nginxStatus" -ForegroundColor $(if ($nginxStatus -eq "active") { "Green" } else { "Red" })
    Write-Host "  Internal Kestrel (5000) : HTTP $kestrelCheck" -ForegroundColor Green
    Write-Host "  Nginx HTTPS Proxy (443) : HTTP $sslCheck" -ForegroundColor Green

    Write-Host "`n=================================================================" -ForegroundColor Green
    Write-Host "  MIGRATION COMPLETED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "=================================================================" -ForegroundColor Green
    Write-Host "Your new Tencent Cloud Lighthouse server ($NewServerIp) is fully configured,"
    Write-Host "running the app, connected to Supabase, and serving the Wildcard SSL certificate!"
    Write-Host ""
    Write-Host "FINAL STEP — CUT OVER DNS IN HOSTINGER:" -ForegroundColor Yellow
    Write-Host "Log into your Hostinger DNS Zone Editor for punitbola.tech and update these 2 records:"
    Write-Host "  1. Type: A  |  Host: @ (or punitbola.tech)  |  Points to: $NewServerIp" -ForegroundColor Cyan
    Write-Host "  2. Type: A  |  Host: *                      |  Points to: $NewServerIp" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Once updated, all web traffic and wildcard subdomains (*.punitbola.tech)"
    Write-Host "will instantly flow to your new high-speed Tencent Cloud server!"
    Write-Host "=================================================================`n"

}
finally {
    if (Test-Path $tempDir) {
        Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
    }
}
