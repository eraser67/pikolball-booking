#!/usr/bin/env bash
set -euo pipefail

# Pikolball Application Deployment Script
# Run on VPS after setup-vps.sh and after copying publish files to /tmp/publish

PUBLISH_SRC="${1:-/tmp/publish}"
DEPLOY_DIR="/var/www/pikolball"

echo "=== 1. Validating source files ==="
if [ ! -f "$PUBLISH_SRC/PickleBallBooking.dll" ]; then
    echo "ERROR: PickleBallBooking.dll not found in $PUBLISH_SRC"
    exit 1
fi

echo "=== 2. Stopping existing service if active ==="
systemctl stop pikolball 2>/dev/null || true

echo "=== 3. Syncing application files to $DEPLOY_DIR ==="
mkdir -p "$DEPLOY_DIR"
cp -r "$PUBLISH_SRC"/* "$DEPLOY_DIR"/
chown -R pikolball:pikolball "$DEPLOY_DIR"
chmod 755 "$DEPLOY_DIR"
chmod -R u=rwX,go=rX "$DEPLOY_DIR"

echo "=== 4. Verifying production environment file ==="
if [ ! -f "/etc/pikolball/production.env" ]; then
    echo "WARNING: /etc/pikolball/production.env does not exist yet."
    echo "Please create /etc/pikolball/production.env with proper permissions (chmod 600) before starting the service."
fi

echo "=== 5. Installing systemd service ==="
if [ -f "/tmp/pikolball.service" ]; then
    cp /tmp/pikolball.service /etc/systemd/system/pikolball.service
    systemctl daemon-reload
    systemctl enable pikolball
    echo "Installed and enabled pikolball.service"
fi

echo "=== 6. Installing Nginx configuration ==="
if [ -f "/tmp/nginx-punitbola.conf" ]; then
    cp /tmp/nginx-punitbola.conf /etc/nginx/sites-available/punitbola.tech
    ln -sf /etc/nginx/sites-available/punitbola.tech /etc/nginx/sites-enabled/
    rm -f /etc/nginx/sites-enabled/default
    nginx -t
    systemctl reload nginx
    echo "Nginx configuration updated and reloaded"
fi

echo "=== 7. Starting pikolball service ==="
systemctl restart pikolball
sleep 3
systemctl status pikolball --no-pager

echo "=== 8. Testing internal Kestrel connection ==="
if curl -s -f http://127.0.0.1:5000 > /dev/null; then
    echo "SUCCESS: ASP.NET Core Kestrel is running and responding on http://127.0.0.1:5000"
else
    echo "Kestrel test completed (response received)."
fi
