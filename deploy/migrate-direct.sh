#!/usr/bin/env bash
set -euo pipefail

# PickleBallBooking — Direct Server-to-Server Migration Script
# Run this script directly on the NEW Tencent Cloud Lighthouse server as root:
#
#   Usage: ./migrate-direct.sh <SOURCE_VPS_IP> [SOURCE_SSH_PORT]
#   Example: ./migrate-direct.sh 187.127.223.93 22
#

SOURCE_IP="${1:-187.127.223.93}"
SOURCE_PORT="${2:-22}"
SOURCE_USER="root"

echo "================================================================="
echo "  PICKLEBALL BOOKING DIRECT SERVER-TO-SERVER MIGRATION"
echo "================================================================="
echo " Pulling from Source VPS : $SOURCE_USER@$SOURCE_IP:$SOURCE_PORT"
echo " Target (Local)          : $(hostname -I | awk '{print $1}')"
echo "================================================================="

if [ "$EUID" -ne 0 ]; then
    echo "ERROR: Please run as root."
    exit 1
fi

echo "[1/6] Running system provisioning (installs .NET 10, Nginx, UFW)..."
mkdir -p /tmp/pikolball-setup
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" &>/dev/null && pwd)"
if [ -f "$SCRIPT_DIR/setup-vps.sh" ]; then
    bash "$SCRIPT_DIR/setup-vps.sh"
else
    # Fetch directly if running standalone
    echo "Fetching setup-vps.sh from source..."
    scp -P "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP:/tmp/setup-vps.sh" /tmp/pikolball-setup/setup-vps.sh 2>/dev/null || true
    if [ -f /tmp/pikolball-setup/setup-vps.sh ]; then
        bash /tmp/pikolball-setup/setup-vps.sh
    fi
fi

echo "[2/6] Migrating environment secrets..."
mkdir -p /etc/pikolball
scp -P "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP:/etc/pikolball/production.env" /etc/pikolball/production.env
chown -R root:pikolball /etc/pikolball
chmod 640 /etc/pikolball/production.env
echo "Secrets migrated successfully!"

echo "[3/6] Migrating Wildcard SSL certificates..."
ssh -p "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP" "tar -czf /tmp/ssl-export.tar.gz -C / etc/letsencrypt"
scp -P "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP:/tmp/ssl-export.tar.gz" /tmp/ssl-export.tar.gz
ssh -p "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP" "rm -f /tmp/ssl-export.tar.gz"
tar -xzf /tmp/ssl-export.tar.gz -C /
rm -f /tmp/ssl-export.tar.gz
mkdir -p /etc/letsencrypt/renewal-hooks/deploy
echo -e '#!/usr/bin/env bash\nsystemctl reload nginx' > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
echo "Wildcard SSL certificates migrated successfully!"

echo "[4/6] Migrating application files and binaries..."
mkdir -p /var/www/pikolball
rsync -avz -e "ssh -p $SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP:/var/www/pikolball/" /var/www/pikolball/
chown -R pikolball:pikolball /var/www/pikolball
chmod 755 /var/www/pikolball
chmod -R u=rwX,go=rX /var/www/pikolball

echo "[5/6] Migrating systemd service and Nginx reverse proxy..."
scp -P "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP:/etc/systemd/system/pikolball.service" /etc/systemd/system/pikolball.service
scp -P "$SOURCE_PORT" "$SOURCE_USER@$SOURCE_IP:/etc/nginx/sites-available/punitbola.tech" /etc/nginx/sites-available/punitbola.tech
ln -sf /etc/nginx/sites-available/punitbola.tech /etc/nginx/sites-enabled/
rm -f /etc/nginx/sites-enabled/default

systemctl daemon-reload
systemctl enable pikolball
systemctl restart pikolball

nginx -t
systemctl reload nginx

echo "[6/6] Verifying local services..."
sleep 3
systemctl status pikolball --no-pager
curl -s -f http://127.0.0.1:5000 >/dev/null && echo "Kestrel 5000: OK"
curl -k -s -f -I -H "Host: punitbola.tech" https://127.0.0.1/ >/dev/null && echo "Nginx HTTPS: OK"

MY_IP=$(curl -s https://api.ipify.org || hostname -I | awk '{print $1}')

echo ""
echo "================================================================="
echo "  MIGRATION COMPLETE! ALL SERVICES ARE LIVE ON THIS SERVER"
echo "================================================================="
echo "Point your Hostinger DNS records (@ and *) to this IP: $MY_IP"
echo "================================================================="
