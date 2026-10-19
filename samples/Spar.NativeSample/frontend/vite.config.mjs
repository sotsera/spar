import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { sparSession } from '../../../tools/Spar.Dev/frontend/vite-session.mjs';

export default defineConfig({
  plugins: [sparSession(), react()],
  cacheDir: '.vite-cache',
  clearScreen: false,
  logLevel: 'error',
});
