import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 3000,
    watch: {
      // This is a monorepo sharing the tree with the .NET solution's own bin/obj build
      // output - without this, a concurrent `dotnet build` crashes Vite's file watcher
      // (EBUSY on its transient .tmp files).
      ignored: ['**/bin/**', '**/obj/**'],
    },
  }
});
