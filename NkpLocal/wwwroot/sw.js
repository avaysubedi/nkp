const CACHE_NAME = 'nkp-pwa-v9';
const ASSETS = [
  './',
  './index.html',
  './style.css?v=9',
  './app.js?v=9',
  './manifest.json'
];

self.addEventListener('install', (e) => {
  e.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      return cache.addAll(ASSETS.map((path) => new URL(path, self.registration.scope).href));
    })
  );
  self.skipWaiting();
});

self.addEventListener('activate', (e) => {
  e.waitUntil(
    caches.keys().then((keys) => {
      return Promise.all(
        keys.map((key) => {
          if (key !== CACHE_NAME) {
            return caches.delete(key);
          }
        })
      );
    })
  );
  self.clients.claim();
});

self.addEventListener('fetch', (e) => {
  const url = new URL(e.request.url);

  // JSON data: always go to the network so GitHub updates are picked up.
  // Offline search uses IndexedDB, not these files.
  if (url.pathname.includes('/data/')) {
    e.respondWith(fetch(e.request));
    return;
  }

  if (e.request.mode === 'navigate') {
    e.respondWith(
      fetch(e.request).catch(() => {
        return caches.match(new URL('./index.html', self.registration.scope));
      })
    );
    return;
  }

  e.respondWith(
    caches.match(e.request).then((cachedResponse) => {
      if (cachedResponse) {
        fetch(e.request).then((networkResponse) => {
          if (networkResponse && networkResponse.status === 200 && e.request.method === 'GET') {
            caches.open(CACHE_NAME).then((cache) => cache.put(e.request, networkResponse));
          }
        }).catch(() => {});
        return cachedResponse;
      }
      return fetch(e.request).then((networkResponse) => {
        if (networkResponse && networkResponse.status === 200 && e.request.method === 'GET') {
          const responseToCache = networkResponse.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put(e.request, responseToCache));
        }
        return networkResponse;
      }).catch(() => {
        if (e.request.headers.get('accept')?.includes('text/html')) {
          return caches.match(new URL('./index.html', self.registration.scope));
        }
      });
    })
  );
});
