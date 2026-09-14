#!/bin/bash
# Builds the four deployable images (signalr-emulator, message-hub, message-receiver,
# message-sender)
# and pushes them to a registry, tagged for docker-compose.registry.yaml's IMAGE_PREFIX
# / IMAGE_TAG variables. Run from the repo root.
#
# Each image is also tagged and pushed as ":latest" alongside its version tag, so
# IMAGE_TAG=latest always resolves to whatever was pushed most recently.
#
# The registry URL comes from .env.registry's IMAGE_PREFIX (gitignored -- copy
# .env.registry.example to .env.registry and fill in real values), the same file
# pull-latest-registry.sh reads, so the two scripts always agree on where images live.
# If .env.registry also sets REGISTRY_USERNAME/REGISTRY_PASSWORD, this logs in before
# pushing (for a registry with auth enabled, e.g. REGISTRY_AUTH=htpasswd).
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
cd "$script_dir"

env_file=".env.registry"

if [ -f "$env_file" ]; then
  set -a
  # shellcheck disable=SC1090
  source "$env_file"
  set +a
fi

if [ -n "${REGISTRY:-}" ]; then
  registry=$REGISTRY
else
  registry=${IMAGE_PREFIX:-}
fi

if [ -z "$registry" ]; then
  echo "No registry URL given (checked \$REGISTRY and IMAGE_PREFIX in $env_file), aborting." >&2
  echo "Copy .env.registry.example to $env_file and fill in your values, or set REGISTRY explicitly." >&2
  exit 1
fi
# Strip a scheme and any trailing slash, so both "https://host/ns/" and "host/ns" work.
registry=${registry#*://}
registry=${registry%/}

if [ -n "${REGISTRY_USERNAME:-}" ] && [ -n "${REGISTRY_PASSWORD:-}" ]; then
  registry_host=${registry%%/*}
  echo "Logging in to $registry_host..."
  echo "$REGISTRY_PASSWORD" | docker login "$registry_host" -u "$REGISTRY_USERNAME" --password-stdin
fi

if [ -n "${TAG:-}" ]; then
  tag=$TAG
else
  read -rp "Image tag [latest]: " tag
  tag=${tag:-latest}
fi

images="signalr-emulator message-hub message-receiver message-sender"
also_latest=$([ "$tag" != "latest" ] && echo true || echo false)

echo
echo "Building $images as $registry/<image>:$tag"
echo

docker build -t "$registry/signalr-emulator:$tag" .
docker build -t "$registry/message-hub:$tag" ./MessageHub
docker build -t "$registry/message-receiver:$tag" \
  -f MessageReceiver/Dockerfile.production ./MessageReceiver
docker build -t "$registry/message-sender:$tag" \
  -f MessageSender/Dockerfile.production ./MessageSender

for image in $images; do
  if [ "$also_latest" = true ]; then
    docker tag "$registry/$image:$tag" "$registry/$image:latest"
  fi
  docker push "$registry/$image:$tag"
  if [ "$also_latest" = true ]; then
    docker push "$registry/$image:latest"
  fi
done

echo
if [ "$also_latest" = true ]; then
  echo "Pushed $tag and latest. Deploy with:"
else
  echo "Pushed. Deploy with:"
fi
echo "  IMAGE_PREFIX=$registry IMAGE_TAG=$tag docker compose -f docker-compose.registry.yaml up -d"
