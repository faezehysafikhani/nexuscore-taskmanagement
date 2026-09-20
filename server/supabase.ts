import { createClient, SupabaseClient } from '@supabase/supabase-js';

export const SUPABASE_TARGET_URL = 'https://nyczzsdkzdscyffbpdun.supabase.co';

export const SUPABASE_EXPECTED_TABLES = [
  'tenants', 'users', 'roles', 'permissions', 'user_roles', 'role_permissions',
  'platform_settings', 'audit_logs', 'tasks', 'events', 'notifications',
  'conversations', 'conversation_participants', 'chat_messages', 'tickets',
  'ticket_comments',
] as const;

let serverSupabaseClient: SupabaseClient | null = null;

/**
 * Lazy initialization of Supabase Server-side Client.
 * Uses SUPABASE_SERVICE_ROLE_KEY or SUPABASE_ANON_KEY.
 * Never throws at load time.
 */
export function getSupabaseAdmin(): SupabaseClient | null {
  if (serverSupabaseClient) {
    return serverSupabaseClient;
  }

  const url = process.env.SUPABASE_URL || SUPABASE_TARGET_URL;
  const key = process.env.SUPABASE_SERVICE_ROLE_KEY || process.env.SUPABASE_ANON_KEY;

  if (!key) {
    return null;
  }

  try {
    serverSupabaseClient = createClient(url, key, {
      auth: {
        persistSession: false,
        autoRefreshToken: false,
      },
    });
    return serverSupabaseClient;
  } catch (error) {
    console.error('[Supabase] Failed to initialize server client:', error);
    return null;
  }
}

/**
 * Test connectivity with Supabase project endpoint
 */
export async function testSupabaseConnection(apiKey?: string): Promise<{
  connected: boolean;
  projectUrl: string;
  hasKey: boolean;
  keyType: 'service_role' | 'anon' | 'none';
  latencyMs: number;
  message: string;
  details?: any;
}> {
  const url = process.env.SUPABASE_URL || SUPABASE_TARGET_URL;
  const configuredKey = process.env.SUPABASE_SERVICE_ROLE_KEY || process.env.SUPABASE_ANON_KEY;
  const key = apiKey?.trim() || configuredKey;
  const keyType = process.env.SUPABASE_SERVICE_ROLE_KEY && !apiKey
    ? 'service_role'
    : process.env.SUPABASE_ANON_KEY
    ? 'anon'
    : 'none';

  const startTime = Date.now();

  try {
    // Attempt standard REST health check ping to the Supabase endpoint
    const response = await fetch(`${url}/rest/v1/`, {
      method: 'GET',
      headers: key ? { apikey: key, Authorization: `Bearer ${key}` } : {},
    });

    const latencyMs = Date.now() - startTime;
    const isOk = response.status === 200 || response.status === 401 || response.status === 404;

    if (key && response.status === 200) {
      return {
        connected: true,
        projectUrl: url,
        hasKey: true,
        keyType,
        latencyMs,
        message: 'اتصال کامل به پایگاه داده Supabase با موفقیت برقرار شد.',
      };
    } else if (key && response.status === 401) {
      return {
        connected: false,
        projectUrl: url,
        hasKey: true,
        keyType,
        latencyMs,
        message: 'کلید احراز هویت Supabase نامعتبر است (Unauthorized). لطفا کلید را بررسی کنید.',
      };
    } else {
      return {
        connected: isOk,
        projectUrl: url,
        hasKey: Boolean(key),
        keyType,
        latencyMs,
        message: key
          ? `پاسخ از پروژه با وضعیت ${response.status} دریافت شد.`
          : 'سرور Supabase در دسترس است. برای خواندن و نوشتن مستقیم داده‌ها، کلید Service Role یا Anon را در تنظیمات وارد کنید.',
      };
    }
  } catch (error: any) {
    const latencyMs = Date.now() - startTime;
    return {
      connected: false,
      projectUrl: url,
      hasKey: Boolean(key),
      keyType,
      latencyMs,
      message: `خطا در ارتباط با سرور Supabase: ${error?.message || 'ارتباط برقرار نشد'}`,
    };
  }
}

export async function checkSupabaseMigration(apiKey?: string) {
  const url = process.env.SUPABASE_URL || SUPABASE_TARGET_URL;
  const key = apiKey?.trim() || process.env.SUPABASE_SERVICE_ROLE_KEY || process.env.SUPABASE_ANON_KEY;
  const startedAt = Date.now();

  if (!key) {
    return {
      projectUrl: url,
      projectReachable: false,
      hasApiKey: false,
      hasValidAuth: false,
      totalTables: SUPABASE_EXPECTED_TABLES.length,
      verifiedCount: 0,
      completionPercentage: 0,
      isComplete: false,
      latencyMs: Date.now() - startedAt,
      message: 'برای بررسی جداول، کلید Anon یا Service Role را وارد کنید.',
      tables: SUPABASE_EXPECTED_TABLES.map((name) => ({
        name,
        module: 'NexusCore',
        description: `جدول ${name}`,
        verified: false,
        httpStatus: 0,
        rowCount: null,
        statusText: 'کلید API تنظیم نشده است',
      })),
    };
  }

  const tables = await Promise.all(SUPABASE_EXPECTED_TABLES.map(async (name) => {
    try {
      const response = await fetch(`${url}/rest/v1/${name}?select=*&limit=1`, {
        headers: { apikey: key, Authorization: `Bearer ${key}`, Prefer: 'count=exact' },
      });
      const contentRange = response.headers.get('content-range');
      const countText = contentRange?.split('/')[1];
      return {
        name,
        module: 'NexusCore',
        description: `جدول ${name}`,
        verified: response.ok,
        httpStatus: response.status,
        rowCount: countText && countText !== '*' ? Number(countText) : null,
        statusText: response.ok ? 'در دسترس' : response.status === 404 ? 'ایجاد نشده' : response.statusText,
      };
    } catch (error: any) {
      return {
        name,
        module: 'NexusCore',
        description: `جدول ${name}`,
        verified: false,
        httpStatus: 0,
        rowCount: null,
        statusText: error?.message || 'ارتباط برقرار نشد',
      };
    }
  }));

  const verifiedCount = tables.filter((table) => table.verified).length;
  const statuses = tables.map((table) => table.httpStatus);
  const hasValidAuth = statuses.some((status) => status !== 401 && status !== 403 && status !== 0);
  const projectReachable = statuses.some((status) => status > 0);

  return {
    projectUrl: url,
    projectReachable,
    hasApiKey: true,
    hasValidAuth,
    totalTables: tables.length,
    verifiedCount,
    completionPercentage: Math.round((verifiedCount / tables.length) * 100),
    isComplete: verifiedCount === tables.length,
    latencyMs: Date.now() - startedAt,
    message: verifiedCount === tables.length
      ? 'تمام جداول مورد انتظار در دسترس هستند.'
      : `${verifiedCount} از ${tables.length} جدول تأیید شد.`,
    tables,
  };
}
