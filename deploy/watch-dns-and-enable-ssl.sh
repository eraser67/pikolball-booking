#!/usr/bin/env bash
echo "Starting DNS watcher for courthauscarmen.tech..."
while true; do
    IP=$(getent hosts courthauscarmen.tech | awk '{print $1}' | head -n 1 || true)
    if [ "$IP" = "201.18.221.53" ]; then
        echo "DNS matched 201.18.221.53! Requesting SSL certificate..."
        /usr/local/bin/enable-ssl-courthauscarmen.sh
        echo "SSL installation completed!"
        break
    fi
    sleep 10
done

