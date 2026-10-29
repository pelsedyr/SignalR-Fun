import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // md-react's CJS build require()s @ariakit/react, which pulls in its
      // legacy CJS use-sync-external-store shim (broken under bundler dep
      // pre-bundling — it does an opaque require("react") that throws).
      // Force resolution straight to ariakit's ESM build, which doesn't
      // carry that shim. Needed even though this app uses no tooltips or menus:
      // dist/index.js is a barrel, and MdLoadingSpinner -- which MdButton's
      // loading state renders -- requires ariakit on its own.
      '@ariakit/react': fileURLToPath(new URL('./node_modules/@ariakit/react/esm/index.js', import.meta.url)),
    },
  },
  server: {
    // This project's port range (18800-18899); MessageReceiver owns 18801.
    port: 18802,
    // Fail loudly instead of sliding to the next free port.
    strictPort: true,
    proxy: {
      // Unset on the host, so `npm run dev` still proxies to localhost:18800.
      // The container sets it, because inside a container localhost is the
      // container itself. Not VITE_-prefixed: this stays server-side and must
      // not be inlined into the client bundle.
      '/api': process.env.API_PROXY_TARGET ?? 'http://localhost:18800',
    },
  },
})
