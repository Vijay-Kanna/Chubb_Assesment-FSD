const routes = [
  ['/api/claims/**', 'https://localhost:7164', /^\/api\/claims/, '', true],
  ['/api/notifications', 'https://localhost:7165', /^\/api/, '', false],
  ['/api/live/**', 'https://localhost:7165', /^\/api\/live/, '/hubs', true],
];

module.exports = Object.fromEntries(routes.map(([path, target, prefix, replacement, ws]) => [
  path,
  { target, secure: true, changeOrigin: true, ws, rewrite: (url) => url.replace(prefix, replacement) },
]));
