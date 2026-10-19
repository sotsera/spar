import net from 'node:net';

// A small Vite adapter: readiness and lifetime only. Vite and the React plugin own HMR.
export function sparSession() {
  const env = process.env;
  let socket;
  let server;
  let stopping = false;
  let buffer = '';
  const configured = !!env.SPAR_DEV_FRONTEND_PIPE;
  const base = configured ? `/${env.SPAR_DEV_HTTP_SCOPE}/` : '/';
  const send = (kind, fields = {}) => {
    socket?.write(JSON.stringify({ kind, token: env.SPAR_DEV_TOKEN, session: env.SPAR_DEV_SESSION,
      processId: process.pid, ...fields }) + '\n');
  };
  const stop = async () => {
    if (stopping) return;
    stopping = true;
    await server.close();
    socket.end();
    process.exitCode = 0;
  };
  return {
    name: 'spar-owned-session',
    apply: 'serve',
    config() {
      return {
        base,
        server: { host: '127.0.0.1', port: Number(env.SPAR_DEV_PORT || 0), strictPort: true,
          cors: false, open: false, forwardConsole: false },
      };
    },
    configureServer(instance) {
      server = instance;
      if (!configured) throw new Error('Start this sample through Spar.Dev so its server has an owning session.');
      server.middlewares.use((request, response, next) => {
        const path = new URL(request.url, 'http://127.0.0.1').pathname;
        if (!path.startsWith(base)) { response.writeHead(403); response.end('Session scope required'); return; }
        response.setHeader('Cache-Control', 'no-store');
        response.setHeader('Referrer-Policy', 'no-referrer');
        if (path === base + '__spar_ready') {
          response.setHeader('X-Spar-Proof', env.SPAR_DEV_CLIENT_CREDENTIAL);
          response.writeHead(200, { 'Content-Type': 'application/json' });
          response.end(JSON.stringify({ session: env.SPAR_DEV_SESSION }));
          return;
        }
        const port = server.httpServer.address().port;
        response.setHeader('Content-Security-Policy', `default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; connect-src 'self' ws://127.0.0.1:${port}; img-src 'self' data:`);
        next();
      });
      socket = net.createConnection(`\\\\.\\pipe\\${env.SPAR_DEV_FRONTEND_PIPE}`);
      socket.setEncoding('utf8');
      const connected = new Promise(resolve => socket.once('connect', () => { send('frontend-hello'); resolve(); }));
      socket.on('data', data => {
        buffer += data;
        if (Buffer.byteLength(buffer, 'utf8') > 16384) { socket.destroy(); return; }
        let boundary;
        while ((boundary = buffer.indexOf('\n')) !== -1) {
          const line = buffer.slice(0, boundary);
          buffer = buffer.slice(boundary + 1);
          let message;
          try { message = JSON.parse(line); } catch { socket.destroy(); return; }
          if (message.kind === 'stop') stop().catch(() => { process.exitCode = 1; socket.destroy(); });
          else if (message.kind === 'backend-update' || message.kind === 'backend-status') {
            server.ws.send({ type: 'custom', event: `spar:${message.kind}`, data: {
              generation: message.generation, revision: message.updateRevision,
              contract: message.contract, document: message.document, detail: message.detail,
            } });
          }
          else if (message.kind === 'probe' && env.SPAR_DEV_PROBE === '1') {
            server.ws.send({ type: 'custom', event: 'spar:sample-probe', data: message.detail });
          }
        }
      });
      socket.on('error', () => { if (!stopping) stop().catch(() => { process.exitCode = 1; }); });
      socket.on('close', () => { if (!stopping) stop().catch(() => { process.exitCode = 1; }); });
      server.httpServer.once('listening', async () => {
        await connected;
        const address = server.httpServer.address();
        send('frontend-ready', { url: `http://127.0.0.1:${address.port}${base}` });
      });
    },
    transformIndexHtml() {
      return [
        { tag: 'meta', attrs: { name: 'spar-client-credential', content: env.SPAR_DEV_CLIENT_CREDENTIAL }, injectTo: 'head' },
        { tag: 'meta', attrs: { name: 'spar-probe', content: env.SPAR_DEV_PROBE || '0' }, injectTo: 'head' },
      ];
    },
    closeServer() {
      socket?.end();
      // A server/config restart ends the session in this first adapter; CSS/component HMR stays in process.
    },
  };
}
