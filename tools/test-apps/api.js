// Sample app for ampm2 testing: small HTTP server that logs a line every second.
const http = require('http');
const port = Number(process.env.PORT || 39871);
let hits = 0;
http.createServer((req, res) => { hits++; res.end('ok ' + hits); }).listen(port, () => console.log(`api listening on ${port}`));
let n = 0;
setInterval(() => {
  n++;
  console.log(`GET /health 200 ${Math.round(Math.random() * 30)}ms  (tick ${n})`);
  if (n % 7 === 0) console.error(`warn: slow upstream response (${900 + n}ms)`);
}, 1000);
