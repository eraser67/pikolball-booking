#!/usr/bin/env bash
set -euo pipefail

# Pikolball Hostinger VPS Provisioning Script
# Run as root on the Hostinger VPS (Ubuntu/Debian)

echo "=== 1. System Inspection ==="
uname -a
cat /etc/os-release
df -h
free -h

echo "=== 2. Creating dedicated application user ==="
if ! id "pikolball" &>/dev/null; then
    useradd --system --shell /usr/sbin/nologin --home-dir /var/www/pikolball pikolball
    echo "Created user pikolball"
else
    echo "User pikolball already exists"
fi

echo "=== 3. Creating application directories ==="
mkdir -p /var/www/pikolball
mkdir -p /etc/pikolball
mkdir -p /var/www/certbot

echo "=== 4. Checking / Installing .NET 10 Runtime ==="
if ! command -v dotnet &>/dev/null || ! dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 10"; then
    echo ".NET 10 runtime not found. Installing Microsoft repository and .NET 10 ASP.NET Core runtime..."
    apt-get update
    apt-get install -y wget apt-transport-https ca-certificates curl gnupg
    
    # Download Microsoft package signing key
    wget -q https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb || true
    if [ -f packages-microsoft-prod.deb ]; then
        dpkg -i packages-microsoft-prod.deb
        rm -f packages-microsoft-prod.deb
    fi
    
    apt-get update
    apt-get install -y aspnetcore-runtime-10.0 || apt-get install -y dotnet-runtime-10.0 || {
        echo "Direct package install not available; installing via dotnet-install.sh script"
        curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
        ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
    }
fi
dotnet --info

echo "=== 5. Checking / Installing Nginx & Certbot ==="
apt-get update
apt-get install -y nginx certbot python3-certbot-nginx ufw

echo "=== 6. Setting permissions on application directory ==="
chown -R pikolball:pikolball /var/www/pikolball
chmod 755 /var/www/pikolball
chown -R root:pikolball /etc/pikolball
chmod 750 /etc/pikolball

echo "=== 7. Configuring Firewall (UFW) ==="
ufw allow 22/tcp comment 'SSH'
ufw allow 80/tcp comment 'HTTP'
ufw allow 443/tcp comment 'HTTPS'
# Ensure Kestrel 5000 is NOT public
ufw delete allow 5000/tcp 2>/dev/null || true
echo "y" | ufw enable || true
ufw status verbose

echo "=== System provisioning completed successfully ==="
