import babel from '@rolldown/plugin-babel'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const outputDirectory = process.env.PLACE_CONTEXT_FRONTEND_OUT_DIR
  ?? '../src/PlaceContext.Host/wwwroot/app'
const apiOrigin = process.env.PLACE_CONTEXT_API_ORIGIN ?? 'http://localhost:7700'

export default defineConfig({
  base: '/app/',
  plugins: [
    react(),
    babel({
      plugins: ['babel-plugin-react-compiler'],
      include: /src\/.*\.[jt]sx$/,
    }),
  ],
  build: {
    outDir: outputDirectory,
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: apiOrigin,
        changeOrigin: true,
      },
      '/auth': {
        target: apiOrigin,
        changeOrigin: true,
      },
      '^/(chart\\.umd\\.js|pcchart\\.js)$': {
        target: apiOrigin,
        changeOrigin: true,
      },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: true,
  },
})
