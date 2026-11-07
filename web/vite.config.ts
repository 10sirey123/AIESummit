import { defineConfig } from 'vite';

const proxy = {
  '/api/v1': 'http://127.0.0.1:18080',
  '/firebase-config': 'http://127.0.0.1:18080',
};

export default defineConfig({
  server: { host: '127.0.0.1', strictPort: true, fs: { allow: ['.', '../docs/assets'] }, proxy },
  preview: { host: '127.0.0.1', strictPort: true, proxy },
});
