// Service Worker for offline functionality
const CACHE_NAME = 'fakeinstants-v1';
const STATIC_CACHE = 'fakeinstants-static-v1';
const AUDIO_CACHE = 'fakeinstants-audio-v1';

const STATIC_FILES = [
    '/',
    '/index.html',
    '/app.css',
    '/fakeinstants.styles.css',
    '/_framework/blazor.webassembly.js',
    '/_framework/dotnet.js',
    '/_framework/dotnet.wasm',
    '/bootstrap/bootstrap.min.css'
];

// Install event - cache static files
self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(STATIC_CACHE)
            .then(cache => cache.addAll(STATIC_FILES))
    );
    self.skipWaiting();
});

// Activate event - cleanup old caches
self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== STATIC_CACHE && cacheName !== AUDIO_CACHE) {
                        return caches.delete(cacheName);
                    }
                })
            );
        })
    );
});

// Fetch event - serve from cache when offline
self.addEventListener('fetch', event => {
    event.respondWith(
        caches.match(event.request)
            .then(response => {
                // Return cached version or fetch from network
                return response || fetch(event.request);
            })
    );
});

