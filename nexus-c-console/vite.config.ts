import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: process.env.VITE_DEV_PROXY_TARGET ?? 'http://127.0.0.1:5000',
        changeOrigin: true,
      },
      '/health': {
        target: process.env.VITE_DEV_PROXY_TARGET ?? 'http://127.0.0.1:5000',
        changeOrigin: true,
      },
      '/openapi': {
        target: process.env.VITE_DEV_PROXY_TARGET ?? 'http://127.0.0.1:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    target: 'es2022',
    sourcemap: false,
    cssCodeSplit: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes('node_modules/react/') || id.includes('node_modules/react-dom/')) {
            return 'vendor-react';
          }
          if (id.includes('node_modules/react-router')) {
            return 'vendor-router';
          }
          if (id.includes('node_modules/@tanstack/react-query')) {
            return 'vendor-query';
          }
          if (id.includes('node_modules/react-hook-form') || id.includes('node_modules/zod')) {
            return 'vendor-forms';
          }
          return undefined;
        },
      },
    },
  },
});
