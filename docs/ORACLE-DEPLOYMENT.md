# MOLY Internal Management — Oracle Production Deployment

This runbook deploys the `InternalManagement.Api` service and its PostgreSQL database. The WPF Desktop client remains installed on Windows workstations. The CSCA-MOLI.STUDIO website remains a separate project until the later admin download integration.

## Target topology

```text
Windows workstations
        │ HTTPS
        ▼
internal-api.molystudio.online → Caddy → InternalManagement.Api:8080
                                             │
                                             ▼
                                  PostgreSQL 17 (private Docker network)
```

Cloudinary remains the file store for receipt attachments. MinIO from the local Compose file is not required for this production topology.

## OCI preparation

1. Create an Ubuntu 24.04 VM in the tenancy home region. An ARM64 Ampere VM is suitable for the container images; confirm the available Always Free quota in the Console before creating it.
2. Assign a Reserved Public IPv4 address.
3. Add DNS `A` record `internal-api.molystudio.online` pointing to that address.
4. Allow inbound TCP `80` and `443` in the OCI security list or network security group. Allow TCP `22` only from the administrator's fixed IP where possible.
5. Keep PostgreSQL port `5432` private. Do not create a public port mapping for it.

## Prepare the server

Install Docker using the official Docker instructions for the selected Ubuntu release, then copy a reviewed release of this repository to a private directory such as `/opt/moli-internal`.

Do not deploy directly from `origin/main` until the current local source has been reviewed and committed. The current repository baseline contains local source files that were not part of the original first commit.

```bash
cd /opt/moli-internal
cp docker/.env.production.example docker/.env.production
chmod 600 docker/.env.production
```

Edit `docker/.env.production` on the server. Set long random values for the database password and `JWT_SECRET`; never copy those values into Git, shell history, screenshots, or tickets.

## Validate and start

Run these commands from the repository root:

```bash
docker compose --env-file docker/.env.production -f docker/docker-compose.production.yml config
docker compose --env-file docker/.env.production -f docker/docker-compose.production.yml up -d --build
docker compose --env-file docker/.env.production -f docker/docker-compose.production.yml ps
```

The API waits for PostgreSQL, then applies the compiled EF migrations because production startup migration is explicitly enabled. Demo data seeding remains disabled.

After DNS has propagated and ports 80/443 are reachable, Caddy obtains the certificate automatically:

```bash
curl --fail https://internal-api.molystudio.online/health/live
curl --fail https://internal-api.molystudio.online/health/ready
curl --fail https://internal-api.molystudio.online/health
```

If startup fails, inspect logs without printing the environment file:

```bash
docker compose --env-file docker/.env.production -f docker/docker-compose.production.yml logs --tail=200 api postgres caddy
```

## Backup and restore

Create a private backup directory and run:

```bash
chmod +x docker/backup-production.sh docker/restore-production.sh
docker/backup-production.sh docker/.env.production /opt/moli-backups
```

Copy backups off the VM and record the SHA-256 value. Restore only after taking a current backup and verifying the target file:

```bash
docker/restore-production.sh docker/.env.production /opt/moli-backups/moli-internal-YYYYMMDD-HHMMSS.dump --confirm
```

Restore should first be tested against a separate verification database or VM. Do not downgrade EF migrations on the live database as a rollback shortcut.

## Release gate

- `internal-api.molystudio.online` resolves to the Reserved IP.
- Only ports 22 (restricted), 80 and 443 are reachable.
- `/health/live` and `/health/ready` pass over HTTPS.
- Login, refresh and logout work from a test Desktop build.
- PostgreSQL backup completes and a restore has been verified.
- No demo credentials, environment files, API keys or tokens are present in the release artifact.
