# Dev-server image, deliberately NOT production-shaped like MessageHub/Dockerfile.
# The point here is Vite's HMR: `docker compose watch` syncs source into the running
# container and Vite hot-reloads it. Only dependency changes rebuild this image.
FROM node:24-slim
WORKDIR /app

# Own layer so editing source doesn't reinstall node_modules.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

EXPOSE 5175
# --host 0.0.0.0 is required: Vite binds loopback only, which is unreachable from
# outside the container. Passing it here rather than in vite.config.js leaves the
# host `npm run dev` behaviour (loopback only) untouched.
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0", "--port", "5175"]
