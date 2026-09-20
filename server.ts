import express, { Request, Response } from 'express';
import cors from 'cors';
import http from 'http';
import path from 'path';
import { createServer as createViteServer } from 'vite';
import {
  checkSupabaseMigration,
  SUPABASE_EXPECTED_TABLES,
  SUPABASE_TARGET_URL,
  testSupabaseConnection,
} from './server/supabase';

const app = express();
const PORT = Number(process.env.PORT) || 3000;
const BACKEND_TARGET = (process.env.VITE_API_BASE_URL || 'http://localhost:5151').replace(/\/+$/, '');

app.use(cors());
app.use(express.json());

// Health check endpoint
app.get('/api/health', (req: Request, res: Response) => {
  res.json({
    status: 'ok',
    mode: 'production-ready-frontend',
    backendTarget: BACKEND_TARGET,
    timestamp: new Date().toISOString(),
  });
});

// Supabase diagnostics belong to the frontend BFF because they target an
// optional external PostgreSQL service rather than the primary .NET database.
app.get('/api/platform/supabase/status', async (_req: Request, res: Response) => {
  const result = await testSupabaseConnection();
  res.json({
    projectUrl: result.projectUrl,
    configured: result.hasKey,
    keyType: result.keyType,
    reachable: result.connected,
    statusCode: result.connected ? 200 : 503,
    latencyMs: result.latencyMs,
    statusDetail: result.message,
    databaseEngine: 'PostgreSQL (Supabase)',
    tablesExpected: SUPABASE_EXPECTED_TABLES,
  });
});

app.post('/api/platform/supabase/check-migration', async (req: Request, res: Response) => {
  res.json(await checkSupabaseMigration(req.body?.apiKey));
});

app.post('/api/platform/supabase/test-connection', async (req: Request, res: Response) => {
  const result = await testSupabaseConnection(req.body?.apiKey);
  res.json({
    success: result.connected,
    statusCode: result.connected ? 200 : 503,
    message: result.message,
    latencyMs: result.latencyMs,
    projectUrl: result.projectUrl || SUPABASE_TARGET_URL,
  });
});

// Proxy any API requests to real .NET Core backend
app.use('/api', async (req: Request, res: Response) => {
  const targetUrl = `${BACKEND_TARGET}/api${req.url}`;
  try {
    const fetchOptions: RequestInit = {
      method: req.method,
      headers: {
        'Content-Type': 'application/json',
        ...(req.headers['authorization'] ? { 'Authorization': req.headers['authorization'] as string } : {}),
        ...(req.headers['x-user-id'] ? { 'X-User-Id': req.headers['x-user-id'] as string } : {}),
      },
    };

    if (['POST', 'PUT', 'PATCH'].includes(req.method) && req.body && Object.keys(req.body).length > 0) {
      fetchOptions.body = JSON.stringify(req.body);
    }

    const backendRes = await fetch(targetUrl, fetchOptions);
    const contentType = backendRes.headers.get('content-type') || '';

    res.status(backendRes.status);
    if (contentType.includes('application/json')) {
      const data = await backendRes.json();
      res.json(data);
    } else {
      const text = await backendRes.text();
      res.send(text);
    }
  } catch (err: any) {
    console.error(`Error forwarding request to real backend ${targetUrl}:`, err.message);
    res.status(502).json({
      error: `عدم دسترسی به سرور دات‌نت کور (${BACKEND_TARGET}): ${err.message}`,
      isSuccess: false,
    });
  }
});

// Start Server with Vite Middleware
async function startServer() {
  const server = http.createServer(app);

  if (process.env.NODE_ENV !== 'production') {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: 'spa',
    });
    app.use(vite.middlewares);
  } else {
    const distPath = path.join(process.cwd(), 'dist');
    app.use(express.static(distPath));
    app.get('*', (req, res) => {
      res.sendFile(path.join(distPath, 'index.html'));
    });
  }

  server.listen(PORT, '0.0.0.0', () => {
    console.log(`NexusCore Frontend running at http://0.0.0.0:${PORT} -> Backend: ${BACKEND_TARGET}`);
  });
}

startServer();
