#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${1:-$ROOT_DIR/docker/.env.production}"
BACKUP_FILE="${2:-}"
CONFIRMATION="${3:-}"
COMPOSE_FILE="$ROOT_DIR/docker/docker-compose.production.yml"

if [[ "$CONFIRMATION" != "--confirm" ]]; then
  echo "Restore replaces data in the production database." >&2
  echo "Run again with: $0 <env-file> <backup-file> --confirm" >&2
  exit 2
fi
if [[ ! -f "$ENV_FILE" || ! -f "$COMPOSE_FILE" || ! -f "$BACKUP_FILE" ]]; then
  echo "Environment, compose, or backup file not found." >&2
  exit 1
fi
if ! command -v docker >/dev/null 2>&1; then
  echo "Docker CLI is required." >&2
  exit 1
fi

docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" exec -T postgres \
  sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore --clean --if-exists --exit-on-error --no-owner --no-privileges --username="$POSTGRES_USER" --dbname="$POSTGRES_DB"' \
  < "$BACKUP_FILE"
