#!/usr/bin/env bash
# Runs on the server, called by the GitHub "Deploy" workflow:  bash deploy.sh <image-tag>
# 1. pulls the new images, 2. updates the database, 3. starts the new version,
# 4. checks /health, 5. goes back to the previous version when the check fails.
set -euo pipefail

NEW_TAG="${1:?image tag}"
APP_DIR=/opt/whatsapp
cd "$APP_DIR"

set_tag() { sed -i "s/^TAG=.*/TAG=$1/" .env; }
current_tag() { grep '^TAG=' .env | cut -d= -f2; }

PREVIOUS_TAG="$(current_tag)"
echo "== Deploying $NEW_TAG (now running: $PREVIOUS_TAG)"

set_tag "$NEW_TAG"
docker compose pull api gateway

echo "== Database"
docker compose up -d postgres
# Migrations only add to the database; the previous version keeps working if we roll back.
docker compose run --rm --no-deps api migrate

echo "== Start"
docker compose up -d --remove-orphans

echo "== Health check"
for i in $(seq 1 40); do
  if curl -fsS --max-time 3 http://127.0.0.1:8080/health >/dev/null; then
    echo "Healthy after $((i * 3)) seconds."
    docker image prune -f >/dev/null
    exit 0
  fi
  sleep 3
done

echo "!! The new version did not become healthy. Logs:"
docker compose logs --tail 80 api gateway || true
if [ -n "$PREVIOUS_TAG" ] && [ "$PREVIOUS_TAG" != "latest" ] && [ "$PREVIOUS_TAG" != "$NEW_TAG" ]; then
  echo "== Rolling back to $PREVIOUS_TAG"
  set_tag "$PREVIOUS_TAG"
  docker compose up -d --remove-orphans
fi
exit 1
