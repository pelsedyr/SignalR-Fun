#!/bin/bash
# Builds the three deployable images (signalr-emulator, message-hub, message-receiver)
# and pushes them to a registry, tagged for docker-compose.registry.yaml's IMAGE_PREFIX
# / IMAGE_TAG variables. Run from the repo root.
#
# Each image is also tagged and pushed as ":latest" alongside its version tag, so
# IMAGE_TAG=latest always resolves to whatever was pushed most recently.
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
cd "$script_dir"

if [ -n "${REGISTRY:-}" ]; then
  registry=$REGISTRY
else
  read -rp "Registry URL (e.g. my-registry.com/signalr-fun): " registry
fi
# Strip a scheme and any trailing slash, so both "https://host/ns/" and "host/ns" work.
registry=${registry#*://}
registry=${registry%/}

if [ -z "$registry" ]; then
  echo "No registry URL given, aborting." >&2
  exit 1
fi

if [ -n "${TAG:-}" ]; then
  tag=$TAG
else
  read -rp "Image tag [latest]: " tag
  tag=${tag:-latest}
fi

images="signalr-emulator message-hub message-receiver"
also_latest=$([ "$tag" != "latest" ] && echo true || echo false)

echo
echo "Building $images as $registry/<image>:$tag"
echo

docker build -t "$registry/signalr-emulator:$tag" .
docker build -t "$registry/message-hub:$tag" ./MessageHub
docker build -t "$registry/message-receiver:$tag" \
  -f MessageReceiver/Dockerfile.production ./MessageReceiver

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
