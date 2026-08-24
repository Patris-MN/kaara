#!/usr/bin/env bash
# Runs the role-provisioning SQL template with variables substituted from the
# container's environment (see infra/docker/.env.example). This script is picked
# up automatically by the official postgres image's /docker-entrypoint-initdb.d
# convention on first cluster initialization only.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

: "${PTS_MIGRATOR_PASSWORD:?PTS_MIGRATOR_PASSWORD must be set}"
: "${PTS_APP_PASSWORD:?PTS_APP_PASSWORD must be set}"

psql -v ON_ERROR_STOP=1 \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  -v migrator_password="$PTS_MIGRATOR_PASSWORD" \
  -v app_password="$PTS_APP_PASSWORD" \
  -v db_name="$POSTGRES_DB" \
  -f "$SCRIPT_DIR/templates/roles.template.sql"
