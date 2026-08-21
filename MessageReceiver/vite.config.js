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
      // carry that shim.
      '@ariakit/react': fileURLToPath(new URL('./node_modules/@ariakit/react/esm/index.js', import.meta.url)),
    },
  },
  server: {
    proxy: {
      '/api': 'http://localhost:7071',
    },
  },
})
