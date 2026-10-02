#!/usr/bin/env bash
set -e

CHALLENGE_FILE="/root/wildcard_challenge.txt"
LOG_FILE="/var/log/certbot_dns.log"

log() {
    echo "$@" | tee -a "$LOG_FILE"
}

log "=========================================================="
log "ACME DNS Challenge for: $CERTBOT_DOMAIN"
log "Remaining challenges : $CERTBOT_REMAINING_CHALLENGES"
log "All domains          : $CERTBOT_ALL_DOMAINS"
log "TXT Record Name      : _acme-challenge"
log "TXT Record FQDN      : _acme-challenge.punitbola.tech"
log "TXT Record Value     : $CERTBOT_VALIDATION"
log "Timestamp            : $(date -u)"
log "=========================================================="

# Also write directly to challenge file for easy reading
cat <<EOF > "$CHALLENGE_FILE"
DOMAIN=$CERTBOT_DOMAIN
RECORD_NAME=_acme-challenge
RECORD_FQDN=_acme-challenge.punitbola.tech
RECORD_VALUE=$CERTBOT_VALIDATION
REMAINING=$CERTBOT_REMAINING_CHALLENGES
STATUS=WAITING_FOR_DNS
EOF

log "[dns-auth-hook] Waiting for TXT record containing: $CERTBOT_VALIDATION"
log "[dns-auth-hook] Polling authoritative nameservers (lunar.dns-parking.com AND solar.dns-parking.com)..."

MAX_RETRIES=240
COUNT=0

while [ $COUNT -lt $MAX_RETRIES ]; do
    TXT_LUNAR=$(dig +short TXT _acme-challenge.punitbola.tech @lunar.dns-parking.com 2>/dev/null || true)
    TXT_SOLAR=$(dig +short TXT _acme-challenge.punitbola.tech @solar.dns-parking.com 2>/dev/null || true)

    # Require BOTH authoritative nameservers to have the record
    if echo "$TXT_LUNAR" | grep -q "$CERTBOT_VALIDATION" && echo "$TXT_SOLAR" | grep -q "$CERTBOT_VALIDATION"; then
        log "[dns-auth-hook] SUCCESS: TXT record verified on BOTH lunar and solar!"
        echo "STATUS=VERIFIED" >> "$CHALLENGE_FILE"
        # 20 second grace period for global propagation
        sleep 20
        exit 0
    fi

    sleep 5
    COUNT=$((COUNT + 1))
    if [ $((COUNT % 6)) -eq 0 ]; then
        log "[dns-auth-hook] Still waiting for sync... ($((COUNT * 5))s elapsed). Lunar: $([ -n "$TXT_LUNAR" ] && echo 'FOUND' || echo 'MISSING'), Solar: $([ -n "$TXT_SOLAR" ] && echo 'FOUND' || echo 'MISSING')"
    fi
done

log "[dns-auth-hook] TIMEOUT: TXT record not detected after 20 minutes."
echo "STATUS=TIMEOUT" >> "$CHALLENGE_FILE"
exit 1
