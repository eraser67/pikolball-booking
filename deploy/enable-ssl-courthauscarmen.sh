#!/usr/bin/env bash
set -euo pipefail

DOMAIN="courthauscarmen.tech"
WWW_DOMAIN="www.courthauscarmen.tech"
EMAIL="admin@punitbola.tech"
VPS_IP="201.18.221.53"

echo "=== 1. Checking DNS resolution for $DOMAIN ==="
RESOLVED_IP=$(getent hosts "$DOMAIN" | awk '{print $1}' | head -n 1 || true)
echo "Resolved IP: '$RESOLVED_IP' (Expected: $VPS_IP)"

if [ "$RESOLVED_IP" != "$VPS_IP" ]; then
    echo "WARNING: $DOMAIN currently resolves to '$RESOLVED_IP', not '$VPS_IP'."
    echo "Please update the A record in Hostinger DNS management to point to $VPS_IP."
    echo "Once updated and propagated, run this script again: /usr/local/bin/enable-ssl-courthauscarmen.sh"
    exit 1
fi

echo "=== 2. Obtaining SSL Certificate from Let's Encrypt ==="
certbot certonly --webroot -w /var/www/certbot \
    -d "$DOMAIN" -d "$WWW_DOMAIN" \
    --non-interactive --agree-tos -m "$EMAIL" || \
certbot certonly --webroot -w /var/www/certbot \
    -d "$DOMAIN" \
    --non-interactive --agree-tos -m "$EMAIL"

if [ ! -f "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]; then
    echo "ERROR: SSL certificate was not created at /etc/letsencrypt/live/$DOMAIN/fullchain.pem"
    exit 1
fi

echo "=== 3. Updating Nginx with SSL Configuration ==="
cat << 'EOF' > /etc/nginx/sites-available/courthauscarmen.tech
server {
    listen 80;
    listen [::]:80;
    server_name courthauscarmen.tech www.courthauscarmen.tech;

    location /.well-known/acme-challenge/ {
        root /var/www/certbot;
    }

    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl;
    listen [::]:443 ssl;
    http2 on;
    server_name courthauscarmen.tech www.courthauscarmen.tech;

    ssl_certificate /etc/letsencrypt/live/courthauscarmen.tech/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/courthauscarmen.tech/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_prefer_server_ciphers on;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_session_cache shared:SSL_COURTHAUS:10m;
    ssl_session_timeout 10m;

    client_max_body_size 10M;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Real-IP $remote_addr;

        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }
}
EOF

nginx -t
systemctl reload nginx
echo "=== SSL successfully configured for $DOMAIN and Nginx reloaded! ==="
