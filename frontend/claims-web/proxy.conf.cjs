const routes = [
  ['/api/claims/**', 'https://localhost:7164', /^\/api\/claims/],
  ['/api/notifications', 'https://localhost:7165', /^\/api/],
];

module.exports = Object.fromEntries(routes.map(([path, target, prefix]) => [
  path,
  { target, secure: true, changeOrigin: true, rewrite: (url) => url.replace(prefix, '') },
]));
