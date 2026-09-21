<#
.SYNOPSIS
    Deploys the published PickleBallBooking application to Hostinger VPS.
.PARAMETER VpsIp
    The public IP address of the Hostinger VPS.
.PARAMETER SshUser
    The SSH username (default: root).
.PARAMETER SshPort
    The SSH port (default: 22).
.PARAMETER SshKeyPath
    Path to the SSH private key (optional).
#>
param (
    [Parameter(Mandatory = $true)]
    [string]$VpsIp,

    [string]$SshUser = "root",
    [int]$SshPort = 22,
    [string]$SshKeyPath = ""
)

$ErrorActionPreference = "Stop"

$sshOpts = @("-p", $SshPort)
$scpOpts = @("-P", $SshPort)

if ($SshKeyPath -ne "") {
    $sshOpts += @("-i", $SshKeyPath)
    $scpOpts += @("-i", $SshKeyPath)
}

$remote = "$SshUser@$VpsIp"

Write-Host "=== 1. Checking SSH connectivity to $remote on port $SshPort ===" -ForegroundColor Cyan
ssh @sshOpts -o ConnectTimeout=10 $remote "echo 'Connected successfully to Hostinger VPS'"

Write-Host "=== 2. Uploading provisioning scripts ===" -ForegroundColor Cyan
scp @scpOpts deploy/setup-vps.sh deploy/deploy-app.sh deploy/pikolball.service deploy/nginx-punitbola.conf "$remote`:/tmp/"

Write-Host "=== 3. Running VPS provisioning on $remote ===" -ForegroundColor Cyan
ssh @sshOpts $remote "chmod +x /tmp/setup-vps.sh /tmp/deploy-app.sh && /tmp/setup-vps.sh"

Write-Host "=== 4. Uploading published build artifacts to VPS ===" -ForegroundColor Cyan
ssh @sshOpts $remote "rm -rf /tmp/publish && mkdir -p /tmp/publish"
scp @scpOpts -r publish/* "$remote`:/tmp/publish/"

Write-Host "=== 5. Deploying application on VPS ===" -ForegroundColor Cyan
ssh @sshOpts $remote "/tmp/deploy-app.sh /tmp/publish"

Write-Host "=== Deployment completed! ===" -ForegroundColor Green
