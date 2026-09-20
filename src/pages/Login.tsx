import React, { useState } from 'react';
import { LogIn, KeyRound, Mail, Building, Trash2, CheckCircle2, AlertCircle } from 'lucide-react';
import { api, AuthTokenStore, PersianMessages } from '../services/api';
import { AuthResponse, UserDto } from '../types';

interface LoginProps {
  onLoginSuccess: (user: UserDto) => void;
  onNavigate: (page: string) => void;
}

export const Login: React.FC<LoginProps> = ({ onLoginSuccess, onNavigate }) => {
  const [email, setEmail] = useState('admin@nexus.local');
  const [password, setPassword] = useState('Admin@12345');
  const [tenantSlug, setTenantSlug] = useState('default');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setMessage(null);

    const result = await api.post<AuthResponse>('/api/identity/auth/login', {
      email,
      password,
      tenantSlug,
    });

    setLoading(false);

    if (result.isSuccess && result.value) {
      AuthTokenStore.set(result.value.accessToken, result.value.refreshToken, result.value.user.id);
      setIsError(false);
      setMessage('ورود با موفقیت انجام شد. در حال هدایت به داشبورد...');
      onLoginSuccess(result.value.user);
      setTimeout(() => {
        onNavigate('dashboard');
      }, 600);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleClearToken = () => {
    AuthTokenStore.clear();
    setIsError(false);
    setMessage('توکن‌های ورود با موفقیت از حافظه مرورگر پاک شد.');
  };

  return (
    <div className="max-w-md mx-auto my-6">
      <div className="bg-white rounded-2xl border border-slate-200 shadow-md p-6 sm:p-8">
        <div className="text-center mb-6">
          <div className="w-12 h-12 rounded-xl bg-blue-600/10 border border-blue-600/20 text-blue-600 flex items-center justify-center mx-auto mb-3">
            <LogIn className="w-6 h-6" />
          </div>
          <h1 className="text-xl font-bold text-slate-900">ورود به سامانه مدیریت نکسوس</h1>
          <p className="text-xs text-slate-500 mt-1">
            اطلاعات حساب کاربری سازمانی خود را وارد نمایید
          </p>
        </div>

        {message && (
          <div
            className={`p-3.5 rounded-lg mb-5 text-xs font-medium flex items-start gap-2.5 ${
              isError
                ? 'bg-rose-50 border border-rose-200 text-rose-800'
                : 'bg-emerald-50 border border-emerald-200 text-emerald-800'
            }`}
          >
            {isError ? (
              <AlertCircle className="w-4 h-4 text-rose-600 shrink-0 mt-0.5" />
            ) : (
              <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0 mt-0.5" />
            )}
            <span>{message}</span>
          </div>
        )}

        <form onSubmit={handleLogin} className="space-y-4">
          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">ایمیل کاربر</label>
            <div className="relative">
              <Mail className="w-4 h-4 text-slate-400 absolute right-3.5 top-3" />
              <input
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="example@domain.com"
                className="input-field pr-10"
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">رمز عبور</label>
            <div className="relative">
              <KeyRound className="w-4 h-4 text-slate-400 absolute right-3.5 top-3" />
              <input
                type="password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••"
                className="input-field pr-10"
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">شناسه سازمان (Tenant Slug)</label>
            <div className="relative">
              <Building className="w-4 h-4 text-slate-400 absolute right-3.5 top-3" />
              <input
                type="text"
                value={tenantSlug}
                onChange={(e) => setTenantSlug(e.target.value)}
                placeholder="default"
                className="input-field pr-10"
              />
            </div>
          </div>

          <div className="pt-2 flex flex-col gap-2.5">
            <button
              type="submit"
              disabled={loading}
              className="w-full btn-primary-nexus py-2.5 text-sm font-semibold"
            >
              <LogIn className="w-4 h-4" />
              <span>{loading ? 'در حال ورود...' : 'ورود به سامانه'}</span>
            </button>

            <button
              type="button"
              onClick={handleClearToken}
              className="w-full btn-secondary-nexus py-2 text-xs text-slate-600 hover:text-rose-600"
            >
              <Trash2 className="w-3.5 h-3.5" />
              <span>پاک کردن توکن از حافظه</span>
            </button>
          </div>
        </form>

        <div className="mt-6 pt-5 border-t border-slate-100 text-center">
          <div className="text-[11px] text-slate-400">
            کاربر پیش‌فرض ادمین: <code className="bg-slate-100 px-1 py-0.5 rounded text-slate-700">admin@nexus.local</code> | رمز: <code className="bg-slate-100 px-1 py-0.5 rounded text-slate-700">Admin@12345</code>
          </div>
        </div>
      </div>
    </div>
  );
};
