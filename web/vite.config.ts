import { resolve } from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const api = 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  build: {
    // Served by the ASP.NET Core app, so one process runs the whole demo.
    outDir: '../src/LiveOverlay.Api/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      input: {
        dashboard: resolve(import.meta.dirname, 'index.html'),
        overlay: resolve(import.meta.dirname, 'overlay.html'),
      },
    },
  },
  server: {
    proxy: {
      '/api': api,
      '/hubs': { target: api, ws: true },
    },
  },
  test: {
    environment: 'node',
  },
})
