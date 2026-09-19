#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${1:-$ROOT_DIR/docker/.env.production}"
OUTPUT_DIR="${2:-$ROOT_DIR/backups}"
COMPOSE_FILE="$ROOT_DIR/docker/docker-compose.production.yml"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Missing production environment file: $ENV_FILE" >&2
  exit 1
fi
if [[ ! -f "$COMPOSE_FILE" ]]; then
  echo "Missing production compose file: $COMPOSE_FILE" >&2
  exit 1
fi
if ! command -v docker >/dev/null 2>&1; then
  echo "Docker CLI is required." >&2
  exit 1
fi

umask 077
mkdir -p "$OUTPUT_DIR"
timestamp="$(date -u +%Y%m%d-%H%M%S)"
backup_file="$OUTPUT_DIR/moli-internal-$timestamp.dump"

if ! docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" exec -T postgres \
  sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_dump --format=custom --no-owner --no-privileges --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  > "$backup_file"; then
  rm -f "$backup_file"
  echo "PostgreSQL backup failed." >&2
  exit 1
fi

sha256sum "$backup_file"
ls -lh "$backup_file"
