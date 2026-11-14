// Usage: node tools/serve.mjs <www folder> [port]
// Loopback-only HTTP server for the disk scenario: serves the www folder, and /blob?size=KB with incompressible,
// cacheable bytes (Cache-Control: public, max-age=86400), so the engine's HTTP disk cache fills. Prints the port.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const root = path.resolve(process.argv[2]);
const port = Number(process.argv[3] || 0);
const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css' };
let blobs = 0;

const server = http.createServer((request, response) => {
  const url = new URL(request.url, 'http://127.0.0.1');
  if (url.pathname === '/blob') {
    const size = Math.max(1, Number(url.searchParams.get('size') || 100)) * 1024;
    blobs++;
    response.writeHead(200, {
      'Content-Type': 'application/octet-stream',
      'Content-Length': size,
      'Cache-Control': 'public, max-age=86400',
      'Last-Modified': new Date(Date.now() - 86400000).toUTCString(),
    });
    response.end(crypto.randomBytes(size));
    return;
  }
  const file = path.join(root, url.pathname === '/' ? 'fill.html' : url.pathname);
  if (!file.startsWith(root) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
    response.writeHead(404);
    response.end();
    return;
  }
  response.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
  response.end(fs.readFileSync(file));
});

server.listen(port, '127.0.0.1', () => console.log(`LISTENING ${server.address().port}`));
process.on('SIGTERM', () => { console.log(`BLOBS ${blobs}`); server.close(); process.exit(0); });
