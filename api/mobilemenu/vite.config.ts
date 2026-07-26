import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { VitePWA } from 'vite-plugin-pwa';

// https://vite.dev/config/
export default defineConfig({
  base: './',
  plugins: [
    react(),
    tailwindcss(),
    VitePWA({
      registerType: 'autoUpdate',
      // Don't auto-inject the SW registration script. We register the service
      // worker ourselves in main.tsx so we can SKIP it inside the admin's
      // <iframe> preview (a cached SW there kept serving a stale index.html and
      // 404'd the old JS hash -> blank mockup after every rebuild).
      injectRegister: false,
      includeAssets: ['favicon.ico', 'apple-touch-icon.png'],
      manifest: {
        name: 'Chay Chaupal',
        short_name: 'Chay Chaupal',
        description: 'Premium Restaurant POS Mobile Menu & Ordering PWA',
        theme_color: '#FAF6F0',
        background_color: '#FAF6F0',
        display: 'standalone',
        orientation: 'portrait',
        scope: '/',
        start_url: '/',
        icons: [
          {
            src: 'https://images.unsplash.com/photo-1541167760496-1628856ab772?w=192&auto=format&fit=crop&q=80',
            sizes: '192x192',
            type: 'image/png'
          },
          {
            src: 'https://images.unsplash.com/photo-1541167760496-1628856ab772?w=512&auto=format&fit=crop&q=80',
            sizes: '512x512',
            type: 'image/png'
          }
        ]
      }
    })
  ]
});
