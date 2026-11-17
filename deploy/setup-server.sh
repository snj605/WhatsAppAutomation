#!/usr/bin/env bash
# One-time setup of a fresh Ubuntu 24.04 server (AWS Lightsail). Run as the default "ubuntu" user:
#   curl -fsSLO https://raw.githubusercontent.com/snj605/WhatsApp_Automation/main/deploy/setup-server.sh
#   bash setup-server.sh <public-host>        for example: bash setup-server.sh 13-233-10-20.sslip.io
# Safe to run again: it skips what is already done and never overwrites an existing .env.
set -euo pipefail

SITE_HOST="${1:?Give the public host name, for example 13-233-10-20.sslip.io}"
APP_DIR=/opt/whatsapp

echo "== 1. System updates and automatic security updates"
sudo apt-get update -y
sudo DEBIAN_FRONTEND=noninteractive apt-get upgrade -y
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y ca-certificates curl gnupg ufw unattended-upgrades openssl
sudo dpkg-reconfigure -f noninteractive unattended-upgrades

echo "== 2. Swap (2 GB): the server has only 2 GB of memory"
if ! swapon --show | grep -q /swapfile; then
  sudo fallocate -l 2G /swapfile
  sudo chmod 600 /swapfile
  sudo mkswap /swapfile
  sudo swapon /swapfile
  echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab >/dev/null
fi
# Use swap only when really needed; keep the file cache.
echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/90-whatsapp.conf >/dev/null
echo 'vm.vfs_cache_pressure=50' | sudo tee -a /etc/sysctl.d/90-whatsapp.conf >/dev/null
sudo sysctl --system >/dev/null

echo "== 3. Docker (official packages)"
if ! command -v docker >/dev/null; then
  sudo install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
  sudo chmod a+r /etc/apt/keyrings/docker.gpg
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
  sudo apt-get update -y
  sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  sudo usermod -aG docker "$USER"
fi
# Docker's own logs stay small too.
echo '{"log-driver":"json-file","log-opts":{"max-size":"10m","max-file":"3"}}' | sudo tee /etc/docker/daemon.json >/dev/null
sudo systemctl restart docker

echo "== 4. Firewall: only SSH, http and https"
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 443/udp
sudo ufw --force enable

echo "== 5. App folder and secrets"
sudo mkdir -p "$APP_DIR/backups"
sudo chown -R "$USER:$USER" "$APP_DIR"
if [ ! -f "$APP_DIR/.env" ]; then
  rand() { openssl rand -base64 "$1" | tr -d '\n'; }
  cat > "$APP_DIR/.env" <<EOF
SITE_HOST=$SITE_HOST
IMAGE_OWNER=snj605
TAG=latest
POSTGRES_PASSWORD=$(openssl rand -hex 24)
AUTH_SIGNING_KEY=$(rand 48)
GATEWAY_SECRET=$(openssl rand -hex 32)
GATEWAY_AUTH_KEY=$(rand 32)
MEDIATR_LICENSE_KEY=
EOF
  chmod 600 "$APP_DIR/.env"
  echo "Created $APP_DIR/.env with new random secrets. Keep a copy somewhere safe (a password manager)."
  echo "Losing GATEWAY_AUTH_KEY means every WhatsApp number must be linked again."
else
  echo "$APP_DIR/.env already exists: left unchanged."
fi

echo "== 6. Daily database backup at 02:30 (kept 14 days)"
( crontab -l 2>/dev/null | grep -v whatsapp-backup ; echo "30 2 * * * bash $APP_DIR/backup.sh >> $APP_DIR/backups/backup.log 2>&1 # whatsapp-backup" ) | crontab -

echo
echo "Done. Next steps (see DEPLOY.md):"
echo "  1. Log out and in again (so 'docker' works without sudo)."
echo "  2. In GitHub, add the repository secrets and run the 'Deploy' workflow."
