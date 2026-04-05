import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5098,
    proxy: {
      '/api': {
        target: 'http://localhost:5097',
        changeOrigin: true,
      },
      '/ledger-hub': {
        target: 'ws://localhost:5097',
        ws: true,
        changeOrigin: true,
      },
    },
  },
})
