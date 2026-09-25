// Demo service for README screenshots: a small HTTP server that logs realistic request lines.
const http = require('http');
const port = Number(process.env.PORT || 39881);
const role = process.env.ROLE || 'api';
const routes = {
  api: ['GET /api/products', 'GET /api/orders/1042', 'POST /api/cart', 'GET /api/health', 'PUT /api/orders/1043/status'],
  web: ['GET /', 'GET /products/blue-hoodie', 'GET /cart', 'GET /assets/app.js', 'GET /checkout'],
  images: ['POST /resize?w=640', 'POST /resize?w=1280', 'GET /cache/stats'],
}[role] || ['GET /'];
const hold = [];
for (let i = 0; i < (role === 'images' ? 30 : 12); i++) hold.push(Buffer.alloc(1024 * 1024, i));
http.createServer((req, res) => res.end('ok')).listen(port, () => console.log(`${role} listening on :${port}`));
let n = 0;
setInterval(() => {
  n++;
  const r = routes[n % routes.length];
  const ms = 3 + Math.round(Math.random() * (role === 'images' ? 180 : 40));
  console.log(`${r} ${r.startsWith('POST') && n % 9 === 0 ? 201 : 200} ${ms}ms`);
  if (n % 23 === 0) console.error(`warn: slow query on orders (${420 + (n % 7) * 31}ms)`);
  if (role === 'images') { const end = Date.now() + 25; let x = 0; while (Date.now() < end) x += Math.sqrt(x + 1); }
}, 700);
