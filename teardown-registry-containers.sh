#!/bin/bash
# Tears down the stack started with:
#   SIGNALR_CLIENT_ENDPOINT=<public HTTPS SignalR URL> \
#   IMAGE_PREFIX=<registry host>/signalr-fun \
#   IMAGE_TAG=<tag> \
#     docker compose -f docker-compose.registry.yaml up -d
#
# Stops and removes the stack's containers, removes every image pulled from the
# registry the stack's containers were actually using (all tags, not just the one
# currently deployed — so leftovers from earlier deploys with a different IMAGE_TAG
# get cleaned up too). The registry host is read straight from the running
# containers, not from IMAGE_PREFIX, so this works even if that var isn't set in
# the current shell. The network is owned by this Compose project, so "down" below
# already removes it; also unsets the env vars used above.
#
# Run `source teardown-registry.sh` (or `. teardown-registry.sh`) instead of
# `./teardown-registry.sh` if you want the env var unset to reach your current
# shell — a script executed normally only unsets them in its own subprocess.

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
compose_file="$script_dir/docker-compose.registry.yaml"

# Only the services whose image comes from IMAGE_PREFIX/IMAGE_TAG — the other services
# (azurite, cosmosdb-emulator, servicebus-emulator, mssql) are fixed Microsoft images that
# are expensive to re-pull and aren't part of what was deployed from the registry.
registry_services="signalr-emulator message-hub message-receiver"

# "docker compose images" reads the containers' actual image metadata, so this reflects
# reality even if IMAGE_PREFIX isn't set in this shell. Column 2 of the table is REPOSITORY;
# the registry host is whatever comes before its first "/".
registry=$(SIGNALR_CLIENT_ENDPOINT="${SIGNALR_CLIENT_ENDPOINT:-unused}" \
  docker compose -f "$compose_file" images $registry_services 2>/dev/null \
  | awk 'NR>1 {print $2; exit}')
registry=${registry%%/*}

echo "Stopping stack..."
if SIGNALR_CLIENT_ENDPOINT="${SIGNALR_CLIENT_ENDPOINT:-unused}" \
  docker compose -f "$compose_file" down; then
  echo "Stack stopped."
else
  echo "Warning: 'docker compose down' failed, continuing with the rest of the teardown." >&2
fi

if [ -n "$registry" ]; then
  echo "Removing images from $registry..."
  # Remove by repository:tag rather than image ID — if two tags share the same underlying
  # image (e.g. a retag), "docker rmi <id>" refuses with "must be forced", but removing
  # each tag reference individually works cleanly.
  images=$(docker images --format '{{.Repository}}:{{.Tag}}' | grep "^$registry/" | sort -u)
  if [ -n "$images" ]; then
    # shellcheck disable=SC2086
    docker rmi $images
  else
    echo "No images found from $registry."
  fi
else
  echo "Could not determine the registry (no containers found for the stack) — skipping image removal."
fi

unset SIGNALR_CLIENT_ENDPOINT IMAGE_PREFIX IMAGE_TAG

if (return 0 2>/dev/null); then
  echo "Done. Env vars unset in this shell."
else
  echo "Done. Re-run with 'source ${BASH_SOURCE[0]}' if you also want SIGNALR_CLIENT_ENDPOINT, IMAGE_PREFIX and IMAGE_TAG unset in your current shell."
fi
