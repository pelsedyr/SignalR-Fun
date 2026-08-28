#!/bin/bash
# Pulls and deploys the ":latest" images for the registry stack:
#   docker compose -f docker-compose.registry.yaml up -d
# with IMAGE_TAG pinned to "latest" and SIGNALR_CLIENT_ENDPOINT/IMAGE_PREFIX read from
# .env.registry (gitignored -- copy .env.registry.example to .env.registry
# and fill in real values).
#
# Runs "pull" before "up -d" on purpose: if a local image already exists tagged "latest",
# "up -d" alone won't re-fetch it even when the registry has a newer one.
#
# If .env.registry also sets REGISTRY_USERNAME/REGISTRY_PASSWORD, this logs in before
# pulling (for a registry with auth enabled, e.g. REGISTRY_AUTH=htpasswd).
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
cd "$script_dir"

env_file=".env.registry"
if [ ! -f "$env_file" ]; then
  echo "Missing $env_file. Copy .env.registry.example to $env_file and fill in your values." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1090
source "$env_file"
set +a

if [ -n "${REGISTRY_USERNAME:-}" ] && [ -n "${REGISTRY_PASSWORD:-}" ]; then
  registry_host=${IMAGE_PREFIX%%/*}
  echo "Logging in to $registry_host..."
  echo "$REGISTRY_PASSWORD" | docker login "$registry_host" -u "$REGISTRY_USERNAME" --password-stdin
fi

export IMAGE_TAG=latest

docker compose --env-file "$env_file" -f docker-compose.registry.yaml pull
docker compose --env-file "$env_file" -f docker-compose.registry.yaml up -d
