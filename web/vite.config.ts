import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    // In development the API runs on :8080; in production both are served from the same origin.
    proxy: { '/api': 'http://localhost:8080', '/health': 'http://localhost:8080' },
  },
  test: {
    environment: 'jsdom',
  },
})
