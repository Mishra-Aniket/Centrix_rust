import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  clearScreen: false,
  envPrefix: ['VITE_', 'TAURI_'],
  plugins: [
    react(),
    tailwindcss()
  ],
  server: {
    port: 5202,
    strictPort: true,
    host: true,
    proxy: {
      '/api': {
        target: 'https://localhost:5201',
        changeOrigin: true,
        secure: false
      },
      '/health': {
        target: 'https://localhost:5201',
        changeOrigin: true,
        secure: false
      }
    }
  }
})

