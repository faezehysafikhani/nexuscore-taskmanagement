import React, { useEffect, useState } from 'react';
import { Building2, PlusCircle, RefreshCw, CheckCircle2, AlertCircle, Globe } from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { TenantDto } from '../types';

export const Tenants: React.FC = () => {
  const [tenants, setTenants] = useState<TenantDto[]>([]);
  const [name, setName] = useState('');
  const [slug, setSlug] = useState('');
  const [description, setDescription] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadTenants();
  }, []);

  const loadTenants = async () => {
    setLoading(true);
    const result = await api.get<TenantDto[]>('/api/platform/tenants');
    setLoading(false);

    if (result.isSuccess && result.value) {
      setTenants(result.value);
      setIsError(false);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleCreateTenant = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name || !slug) {
      setIsError(true);
      setMessage('نام سازمان و شناسه یکتا (Slug) الزامی است.');
      return;
    }

    setLoading(true);
    const result = await api.post<TenantDto>('/api/platform/tenants', {
      name,
      slug,
      description,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage('سازمان جدید با موفقیت در سامانه ایجاد شد.');
      setName('');
      setSlug('');
      setDescription('');
      loadTenants();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2 border-b border-slate-200">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-violet-600/10 text-violet-600 rounded-xl">
            <Building2 className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">سازمان‌ها و مستاجران (Tenants)</h1>
            <p className="text-xs text-slate-500">مدیریت فضای تفکیک‌شده سازمان‌های عضو پلتفرم نکسوس</p>
          </div>
        </div>
        <button
          onClick={loadTenants}
          disabled={loading}
          className="btn-secondary-nexus text-xs"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>به‌روزرسانی سازمان‌ها</span>
        </button>
      </div>

      {/* Alert Message */}
      {message && (
        <div
          className={`p-3.5 rounded-lg text-xs font-medium flex items-center justify-between gap-2 ${
            isError
              ? 'bg-rose-50 border border-rose-200 text-rose-800'
              : 'bg-emerald-50 border border-emerald-200 text-emerald-800'
          }`}
        >
          <div className="flex items-center gap-2">
            {isError ? (
              <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
            ) : (
              <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
            )}
            <span>{message}</span>
          </div>
          <button
            onClick={() => setMessage(null)}
            className="text-slate-400 hover:text-slate-600 text-xs px-2"
          >
            ×
          </button>
        </div>
      )}

      {/* Create Tenant Form */}
      <div className="form-panel">
        <div className="flex items-center gap-2 font-bold text-sm text-slate-800 mb-4 pb-2 border-b border-slate-100 w-full">
          <PlusCircle className="w-4 h-4 text-violet-600" />
          <span>ثبت سازمان یا شرکت جدید</span>
        </div>
        <form onSubmit={handleCreateTenant} className="w-full grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">نام سازمان</label>
            <input
              type="text"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="مثال: شرکت توسعه فناوری البرز"
              className="input-field"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">شناسه یکتا (Slug)</label>
            <input
              type="text"
              required
              value={slug}
              onChange={(e) => setSlug(e.target.value.toLowerCase().replace(/\s+/g, '-'))}
              placeholder="alborz-tech"
              className="input-field font-mono text-xs"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">توضیحات سازمان</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="شرح فعالیت یا حوزه سازمان..."
              className="input-field"
            />
          </div>

          <div>
            <button
              type="submit"
              disabled={loading}
              className="w-full btn-primary-nexus py-2 text-xs bg-violet-600 hover:bg-violet-700"
            >
              <PlusCircle className="w-3.5 h-3.5" />
              <span>ایجاد سازمان</span>
            </button>
          </div>
        </form>
      </div>

      {/* Tenants Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-slate-100 font-bold text-sm text-slate-800">
          سازمان‌های ثبت‌شده ({tenants.length} سازمان)
        </div>
        <div className="overflow-x-auto">
          <table className="table-nexus">
            <thead>
              <tr>
                <th>نام سازمان</th>
                <th>شناسه یکتا (Slug)</th>
                <th>توضیحات</th>
                <th>وضعیت</th>
                <th>شناسه سیستمی (Tenant ID)</th>
              </tr>
            </thead>
            <tbody>
              {tenants.length > 0 ? (
                tenants.map((t) => (
                  <tr key={t.id}>
                    <td className="font-bold text-slate-900">{t.name}</td>
                    <td>
                      <span className="font-mono text-xs px-2 py-0.5 rounded bg-slate-100 text-slate-700 border border-slate-200">
                        {t.slug}
                      </span>
                    </td>
                    <td className="text-xs text-slate-600">{t.description || '-'}</td>
                    <td>
                      <span
                        className={`px-2.5 py-0.5 rounded-full text-xs font-semibold border ${
                          t.isActive
                            ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                            : 'bg-slate-100 text-slate-500 border-slate-200'
                        }`}
                      >
                        {t.isActive ? 'فعال' : 'غیرفعال'}
                      </span>
                    </td>
                    <td className="font-mono text-xs text-slate-500">{t.id}</td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={5} className="py-8 text-center text-slate-400 text-xs">
                    {loading ? 'در حال بارگذاری سازمان‌ها...' : 'هیچ سازمانی ثبت نشده است.'}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};
