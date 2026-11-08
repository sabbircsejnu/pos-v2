// Exchange UAT web server (local only, no dependencies).
// Serves the Angular development build from .uat/web/browser on http://127.0.0.1:4200 (the origin the API's CORS policy
// already allows) and points the compiled API base URL at the UAT API on http://127.0.0.1:5105, so the normal
// Development API port 5005 is never used. No project file is changed.
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = fileURLToPath(new URL('.', import.meta.url));
const root = resolve(here, '.uat', 'web', 'browser');
const host = '127.0.0.1';
const port = Number(process.env.UAT_WEB_PORT ?? 4200);
const apiFrom = 'http://localhost:5005/api';
const apiTo = process.env.UAT_API_URL ?? 'http://127.0.0.1:5105/api';
const types = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
  '.json': 'application/json', '.svg': 'image/svg+xml', '.png': 'image/png', '.ico': 'image/x-icon',
  '.woff2': 'font/woff2', '.woff': 'font/woff', '.ttf': 'font/ttf', '.map': 'application/json', '.txt': 'text/plain'
};

async function fileFor(urlPath) {
  const clean = normalize(decodeURIComponent(urlPath.split('?')[0])).replace(/^([/\\])+/, '');
  const candidate = join(root, clean);
  if (!candidate.startsWith(root)) return null;
  try {
    if ((await stat(candidate)).isFile()) return candidate;
  } catch { /* fall through to the SPA index */ }
  return extname(clean) ? null : join(root, 'index.html');
}

createServer(async (request, response) => {
  const path = await fileFor(request.url ?? '/');
  if (!path) {
    response.writeHead(404).end('Not found');
    return;
  }
  let body = await readFile(path);
  const type = types[extname(path)] ?? 'application/octet-stream';
  if (type.startsWith('text/javascript')) body = Buffer.from(body.toString('utf8').replaceAll(apiFrom, apiTo));
  response.writeHead(200, { 'Content-Type': type, 'Cache-Control': 'no-store' }).end(body);
}).listen(port, host, () => console.log(`Exchange UAT web on http://${host}:${port} -> API ${apiTo}`));
