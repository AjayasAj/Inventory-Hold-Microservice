import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

// In development the API is proxied so the browser sees a single origin, matching the nginx setup in Docker.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': process.env.API_PROXY_TARGET ?? 'http://localhost:8080',
    },
  },
});
