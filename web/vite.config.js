import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

const gateway = 'http://127.0.0.1:5000';

/** Routes servies par la passerelle : jamais la coquille de l'application. */
const serverPaths = ['/graphql', '/auth', '/media', '/archive-media', '/covers', '/img', '/preview'];

export default defineConfig({
  plugins: [
    react(),
    // Coquille, scripts et jaquettes mis en cache : l'application s'ouvre sans réseau.
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: false,
      manifest: false,
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,webmanifest}'],
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/(graphql|auth|media|archive-media|preview)(\/|$|\?)/, /\/[^/?]+\.[^/]+$/],
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          {
            urlPattern: ({ url, sameOrigin }) =>
              sameOrigin && (url.pathname.startsWith('/covers/') || url.pathname.startsWith('/img')),
            handler: 'CacheFirst',
            options: {
              cacheName: 'dk-covers',
              expiration: { maxEntries: 4000, maxAgeSeconds: 90 * 24 * 3600 },
              cacheableResponse: { statuses: [200] },
            },
          },
          {
            urlPattern: ({ url, sameOrigin }) =>
              sameOrigin && /\.(svg|cur|jpg|jpeg|png|apng|webp|avif|gif|otf|ttf|woff|woff2)$/.test(url.pathname),
            handler: 'StaleWhileRevalidate',
            options: { cacheName: 'dk-assets', cacheableResponse: { statuses: [200] } },
          },
          {
            urlPattern: /^https:\/\/fonts\.(googleapis|gstatic)\.com\//,
            handler: 'StaleWhileRevalidate',
            options: { cacheName: 'dk-fonts', cacheableResponse: { statuses: [0, 200] } },
          },
        ],
      },
    }),
  ],
  server: {
    port: 4200,
    proxy: Object.fromEntries(
      serverPaths.map(path => [path, { target: gateway, ws: path === '/graphql', changeOrigin: false }]),
    ),
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.js'],
    restoreMocks: true,
    css: false,
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    chunkSizeWarningLimit: 900,
  },
});
