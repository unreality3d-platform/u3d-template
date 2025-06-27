const CACHE_NAME = 'unity-webgl-v1';
const urlsToCache = [
  './',
  './index.html',
  './Build/u3d-sdk-template.loader.js',
  './Build/u3d-sdk-template.framework.js',
  './Build/u3d-sdk-template.data',
  './Build/u3d-sdk-template.wasm'
];

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then((cache) => cache.addAll(urlsToCache))
  );
});

self.addEventListener('fetch', (event) => {
  event.respondWith(
    caches.match(event.request)
      .then((response) => {
        return response || fetch(event.request);
      })
  );
});