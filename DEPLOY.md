# Deploying WhatsApp Automation (AWS Lightsail + GitHub Actions)

One small server runs everything with Docker Compose:

```
Internet ──443/80──► Caddy (HTTPS, compression, blocks /internal)
                       └─► API + website (.NET 10)  ◄──► WhatsApp gateway (Node, private)
                                   └──────────────────► PostgreSQL 17 (private, daily backups)
```

- **CI** (`.github/workflows/ci.yml`): on every push and pull request. It checks that there are no secrets in git, then builds and tests the API, website, gateway and mobile app.
- **Deploy** (`deploy.yml`): after CI passes on `main`. It builds the images, pushes them to `ghcr.io`, copies the server files over, migrates the database, starts the new version, checks `/health`, and **rolls back** if the check fails.
- **Mobile release** (`mobile-release.yml`): push a tag `v1.0.0` and a signed APK appears under **Releases**.

The repository is public. **No secret is ever in git.** They live in GitHub Secrets and in `/opt/whatsapp/.env` on the server.

---

## 1. Lightsail (once)

1. **Static IP**: Lightsail > Networking > Create static IP > attach it to `Ubuntu-1`. Write it down, for example `13.233.10.20`.
2. **Firewall**: instance > Networking > IPv4 firewall. Keep SSH (22) and HTTP (80), and add **HTTPS (443, TCP)**.
3. **Public address without a domain**: use `13-233-10-20.sslip.io` (your IP with dashes). It points to your IP automatically, and Caddy gets a real HTTPS certificate for it.
   When you buy a domain later, point an A record at the static IP and put the domain in `SITE_HOST` (in `.env` and in GitHub).
4. **Snapshots**: instance > Snapshots > enable **automatic snapshots** (daily, kept 7 days).

## 2. Server setup (once)

Connect (Lightsail > Connect using SSH, or `ssh ubuntu@<ip>`), then:

```bash
curl -fsSLO https://raw.githubusercontent.com/snj605/WhatsApp_Automation/main/deploy/setup-server.sh
bash setup-server.sh 13-233-10-20.sslip.io
exit        # log in again so docker works without sudo
```

This does the following:
- installs Docker and automatic security updates;
- adds 2 GB swap;
- turns on the firewall (22, 80, 443);
- creates `/opt/whatsapp/.env` with **new random secrets**;
- sets the daily backup at 02:30.

**Copy `/opt/whatsapp/.env` into your password manager.** If `GATEWAY_AUTH_KEY` is lost, every WhatsApp number must be linked again.

## 3. Deploy key for GitHub (once)

On your PC (PowerShell or Git Bash):

```bash
ssh-keygen -t ed25519 -f whatsapp_deploy -N "" -C "github-deploy"
```

1. Add the **public** key on the server:
   `echo "<contents of whatsapp_deploy.pub>" >> ~/.ssh/authorized_keys`
2. Get the server's host key: `ssh-keyscan -t ed25519 <static-ip>` (copy the whole line).

## 4. GitHub settings (once)

Repository > Settings:

**Environments** > New environment `production`. Optionally add yourself as a required reviewer, so every deployment waits for your click.

**Secrets and variables > Actions > Secrets:**

| Name | Value |
|---|---|
| `DEPLOY_HOST` | static IP, e.g. `13.233.10.20` |
| `DEPLOY_USER` | `ubuntu` |
| `DEPLOY_SSH_KEY` | contents of `whatsapp_deploy` (the private key file) |
| `DEPLOY_KNOWN_HOSTS` | the line from `ssh-keyscan` |
| `PRIMENG_LICENSE_KEY` | your PrimeNG key (from your PrimeUI account) |
| `ANDROID_KEYSTORE_BASE64` | see step 7 |
| `ANDROID_KEYSTORE_PASSWORD` | see step 7 |
| `ANDROID_KEY_ALIAS` | `upload` |
| `ANDROID_KEY_PASSWORD` | see step 7 |

**Variables:** `SITE_HOST` = `13-233-10-20.sslip.io`

Delete `whatsapp_deploy` from your PC once it is in GitHub, or keep it in your password manager.

## 5. First deployment

Push to `main`. CI runs first (about 10 minutes), then Deploy. The first deployment also gets the HTTPS certificate. Then open `https://13-233-10-20.sslip.io`.

Create the first account (the password is asked twice and not shown). Owners need at least 12 characters:

```bash
cd /opt/whatsapp
docker compose run --rm -it api create-user --username samir --display-name "Samir" --role Owner
```

**Link a test WhatsApp number first**, not a client's. WhatsApp may restrict numbers whose linked device runs from a cloud server's IP.

## 6. Every change after that

Push to `main`. CI checks it, and Deploy puts it live with no downtime for the database. A failed health check rolls back automatically. You can also start a deployment by hand: Actions > Deploy > Run workflow.

## 7. Android APK for demo clients

Create the signing key **once** and keep it safe. Losing it means users must uninstall to update.

```bash
keytool -genkeypair -v -keystore upload.jks -alias upload -keyalg RSA -keysize 2048 -validity 10000
# PowerShell: [Convert]::ToBase64String([IO.File]::ReadAllBytes("upload.jks")) | Set-Clipboard
# Git Bash:   base64 -w0 upload.jks
```

Put the base64 text in `ANDROID_KEYSTORE_BASE64` and the two passwords in their secrets. Then:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

The APK appears on the repository's **Releases** page, built for `https://<SITE_HOST>`. Send clients that link. They allow "Install unknown apps" once.

## 8. Day-to-day on the server

```bash
cd /opt/whatsapp
docker compose ps                         # what runs
docker compose logs -f --tail 100 api     # API log (also: gateway, caddy, postgres)
docker stats --no-stream                  # memory per service
bash backup.sh                            # backup now
ls -lh backups/                           # backups (14 days)
```

**Restore a backup** (replaces the current data):

```bash
docker compose stop api gateway
docker compose exec -T postgres pg_restore -U whatsapp -d whatsapp --clean --if-exists < backups/<file>.dump
docker compose start api gateway
```

**Go back to an older version by hand:** set `TAG=<12-character commit id>` in `.env`, then `docker compose up -d`.

## Memory and speed (2 GB server)

| Service | Limit | Tuning |
|---|---|---|
| PostgreSQL | 384 MB | `shared_buffers` 96 MB, 40 connections, slow queries (> 500 ms) logged |
| API | 640 MB | workstation GC with memory conserve, Tiered PGO, small chiseled image without a shell |
| Gateway | 384 MB | Node heap 256 MB, production dependencies only |
| Caddy | 96 MB | zstd/gzip compression, HTTP/2 and HTTP/3, 1-year cache for hashed website files |

There's also 2 GB swap, kept for emergencies only (`swappiness=10`), and logs capped at 30 MB per service.
