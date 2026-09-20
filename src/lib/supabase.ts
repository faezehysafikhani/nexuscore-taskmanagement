import { createClient, SupabaseClient } from '@supabase/supabase-js';

export const SUPABASE_DEFAULT_URL = 'https://nyczzsdkzdscyffbpdun.supabase.co';

let supabaseClientInstance: SupabaseClient | null = null;

/**
 * Lazy initialization of Supabase client on the frontend.
 * Uses environment variable VITE_SUPABASE_ANON_KEY or falls back gracefully.
 */
export function getSupabaseClient(): SupabaseClient | null {
  if (supabaseClientInstance) {
    return supabaseClientInstance;
  }

  const env = (import.meta as any).env || {};
  const supabaseUrl = env.VITE_SUPABASE_URL || SUPABASE_DEFAULT_URL;
  const supabaseAnonKey = env.VITE_SUPABASE_ANON_KEY;

  if (!supabaseAnonKey) {
    return null;
  }

  try {
    supabaseClientInstance = createClient(supabaseUrl, supabaseAnonKey);
    return supabaseClientInstance;
  } catch (err) {
    console.error('Failed to initialize Supabase client:', err);
    return null;
  }
}

export function isSupabaseClientConfigured(): boolean {
  const env = (import.meta as any).env || {};
  return Boolean(env.VITE_SUPABASE_ANON_KEY);
}
