#!/usr/bin/env bash
# Daily database backup (cron, set up by setup-server.sh). Keeps 14 days in /opt/whatsapp/backups.
# Restore one:  docker compose exec -T postgres pg_restore -U whatsapp -d whatsapp --clean --if-exists < backups/<file>.dump
# Also take a Lightsail snapshot of the whole server once a week (Lightsail console > Snapshots > Automatic).
set -euo pipefail
cd /opt/whatsapp
FILE="backups/whatsapp-$(date +%Y-%m-%d_%H%M).dump"
docker compose exec -T postgres pg_dump -U whatsapp -d whatsapp -Fc > "$FILE"
# A backup that cannot be read is no backup: check it lists its contents.
docker compose exec -T postgres pg_restore --list < "$FILE" >/dev/null
find backups -name 'whatsapp-*.dump' -mtime +14 -delete
echo "$(date -Is) backup ok: $FILE ($(du -h "$FILE" | cut -f1))"
