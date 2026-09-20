import { ApiResult } from '../types';

export const API_BASE_URL: string = (
  (typeof import.meta !== 'undefined' && import.meta.env && import.meta.env.VITE_API_BASE_URL) ||
  'http://localhost:5151'
).replace(/\/+$/, '');

export class AuthTokenStore {
  private static readonly ACCESS_TOKEN_KEY = 'nexus_access_token';
  private static readonly REFRESH_TOKEN_KEY = 'nexus_refresh_token';
  private static readonly USER_ID_KEY = 'nexus_user_id';

  static getAccessToken(): string | null {
    try {
      return localStorage.getItem(this.ACCESS_TOKEN_KEY);
    } catch {
      return null;
    }
  }

  static getUserId(): string | null {
    try {
      return localStorage.getItem(this.USER_ID_KEY);
    } catch {
      return null;
    }
  }

  static set(accessToken: string, refreshToken?: string, userId?: string) {
    try {
      localStorage.setItem(this.ACCESS_TOKEN_KEY, accessToken);
      if (refreshToken) {
        localStorage.setItem(this.REFRESH_TOKEN_KEY, refreshToken);
      }
      if (userId) {
        localStorage.setItem(this.USER_ID_KEY, userId);
      }
    } catch (e) {
      console.warn('Failed to store auth token', e);
    }
  }

  static clear() {
    try {
      localStorage.removeItem(this.ACCESS_TOKEN_KEY);
      localStorage.removeItem(this.REFRESH_TOKEN_KEY);
      localStorage.removeItem(this.USER_ID_KEY);
    } catch (e) {
      console.warn('Failed to clear auth token', e);
    }
  }
}

export class PersianMessages {
  static error(error?: string | null): string {
    if (!error) {
      return 'عملیات انجام نشد. لطفاً دوباره تلاش کنید.';
    }

    if (error.toLowerCase().includes('invalid email or password')) {
      return 'ایمیل یا رمز عبور نادرست است.';
    }

    if (
      error.toLowerCase().includes('authentication is required') ||
      error.toLowerCase().includes('unauthorized') ||
      error.includes('401')
    ) {
      return 'برای انجام این عملیات ابتدا وارد شوید.';
    }

    if (error.toLowerCase().includes('not found') || error.includes('404')) {
      return 'رکورد موردنظر پیدا نشد.';
    }

    if (
      error.toLowerCase().includes('already exists') ||
      error.toLowerCase().includes('conflict') ||
      error.includes('409')
    ) {
      return 'رکوردی با این مشخصات قبلاً ثبت شده است.';
    }

    if (
      error.toLowerCase().includes('validation') ||
      error.toLowerCase().includes('required') ||
      error.includes('400')
    ) {
      return 'اطلاعات واردشده معتبر نیست. لطفاً فیلدها را بررسی کنید.';
    }

    return error;
  }
}

export async function apiRequest<T = any>(
  endpoint: string,
  options: RequestInit = {}
): Promise<ApiResult<T>> {
  try {
    const token = AuthTokenStore.getAccessToken();
    const userId = AuthTokenStore.getUserId();
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...(options.headers as Record<string, string> || {}),
    };

    if (token) {
      headers['Authorization'] = `Bearer ${token}`;
    }
    if (userId) {
      headers['X-User-Id'] = userId;
    }

    // Relative to the page's own origin (not API_BASE_URL) so the request always lands on
    // this app's own server.ts, which proxies /api to the real backend server-side. That
    // keeps API calls working no matter which host/IP the browser loaded the page from -
    // API_BASE_URL is only meaningful on the server that runs the proxy (see server.ts).
    const fullUrl = endpoint.startsWith('http://') || endpoint.startsWith('https://')
      ? endpoint
      : `${endpoint.startsWith('/') ? '' : '/'}${endpoint}`;

    const response = await fetch(fullUrl, {
      ...options,
      headers,
    });

    if (!response.ok) {
      let errMsg = `خطای سرور (${response.status}: ${response.statusText})`;
      const rawText = await response.text();
      if (rawText) {
        try {
          const errJson = JSON.parse(rawText);
          errMsg = errJson.error || errJson.message || errJson.title || errJson.detail || errMsg;
        } catch {
          errMsg = rawText;
        }
      }
      return {
        isSuccess: false,
        error: errMsg,
      };
    }

    if (response.status === 204) {
      return {
        isSuccess: true,
        value: null as any,
      };
    }

    const text = await response.text();
    const data = text ? JSON.parse(text) : null;
    return {
      isSuccess: true,
      value: data,
    };
  } catch (err: any) {
    return {
      isSuccess: false,
      error: err.message || 'خطا در برقراری ارتباط با سرور .NET Core',
    };
  }
}

export const api = {
  get: <T = any>(url: string) => apiRequest<T>(url, { method: 'GET' }),
  post: <T = any>(url: string, body?: any) =>
    apiRequest<T>(url, {
      method: 'POST',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
  put: <T = any>(url: string, body?: any) =>
    apiRequest<T>(url, {
      method: 'PUT',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
  patch: <T = any>(url: string, body?: any) =>
    apiRequest<T>(url, {
      method: 'PATCH',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    }),
  delete: <T = any>(url: string) => apiRequest<T>(url, { method: 'DELETE' }),
};
