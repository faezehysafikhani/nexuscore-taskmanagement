import React, { useEffect, useState } from 'react';
import { Shield, ShieldPlus, RefreshCw, CheckCircle2, AlertCircle, Key } from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { RoleDto } from '../types';

export const Roles: React.FC = () => {
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [tenantId, setTenantId] = useState('11111111-1111-1111-1111-111111111111');
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadRoles();
  }, []);

  const loadRoles = async () => {
    setLoading(true);
    const result = await api.get<RoleDto[]>(`/api/identity/roles?tenantId=${tenantId}`);
    setLoading(false);

    if (result.isSuccess && result.value) {
      setRoles(result.value);
      setIsError(false);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleCreateRole = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!name) {
      setIsError(true);
      setMessage('نام نقش اجباری است.');
      return;
    }

    setLoading(true);
    const result = await api.post<RoleDto>('/api/identity/roles', {
      tenantId,
      name,
      description,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage('نقش با موفقیت ایجاد شد.');
      setName('');
      setDescription('');
      loadRoles();
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
          <div className="p-2.5 bg-indigo-600/10 text-indigo-600 rounded-xl">
            <Shield className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">مدیریت نقش‌ها (Roles)</h1>
            <p className="text-xs text-slate-500">تعریف نقش‌های کاربری، تعیین سطح دسترسی و نقش‌های سیستمی</p>
          </div>
        </div>
        <button
          onClick={loadRoles}
          disabled={loading}
          className="btn-secondary-nexus text-xs"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>به‌روزرسانی جدول</span>
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

      {/* Create Role Form Panel */}
      <div className="form-panel">
        <div className="flex items-center gap-2 font-bold text-sm text-slate-800 mb-4 pb-2 border-b border-slate-100 w-full">
          <ShieldPlus className="w-4 h-4 text-indigo-600" />
          <span>تعریف نقش جدید</span>
        </div>
        <form onSubmit={handleCreateRole} className="w-full grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end">
          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">شناسه سازمان</label>
            <input
              type="text"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              placeholder="Tenant ID"
              className="input-field font-mono text-xs"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">نام نقش</label>
            <input
              type="text"
              required
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="مثال: SupportSpecialist"
              className="input-field"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">توضیحات</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="شرح مسئولیت‌ها و حدود دسترسی..."
              className="input-field"
            />
          </div>

          <div>
            <button
              type="submit"
              disabled={loading}
              className="w-full btn-primary-nexus py-2 text-xs bg-indigo-600 hover:bg-indigo-700"
            >
              <ShieldPlus className="w-3.5 h-3.5" />
              <span>ایجاد نقش</span>
            </button>
          </div>
        </form>
      </div>

      {/* Roles Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-slate-100 font-bold text-sm text-slate-800">
          فهرست نقش‌های فعال ({roles.length} نقش)
        </div>
        <div className="overflow-x-auto">
          <table className="table-nexus">
            <thead>
              <tr>
                <th>نام نقش</th>
                <th>توضیحات</th>
                <th>نوع نقش</th>
                <th>تعداد و فهرست دسترسی‌ها</th>
              </tr>
            </thead>
            <tbody>
              {roles.length > 0 ? (
                roles.map((role) => (
                  <tr key={role.id}>
                    <td className="font-semibold text-slate-900">{role.name}</td>
                    <td className="text-xs text-slate-600">{role.description || '-'}</td>
                    <td>
                      <span
                        className={`px-2.5 py-0.5 rounded-full text-xs font-semibold border ${
                          role.isSystem
                            ? 'bg-amber-50 text-amber-800 border-amber-200'
                            : 'bg-blue-50 text-blue-700 border-blue-200'
                        }`}
                      >
                        {role.isSystem ? 'سیستمی (Protected)' : 'سفارشی'}
                      </span>
                    </td>
                    <td>
                      <div className="flex flex-wrap gap-1 max-w-lg">
                        {role.permissions && role.permissions.length > 0 ? (
                          role.permissions.map((p, i) => (
                            <span
                              key={i}
                              className="px-2 py-0.5 text-[11px] rounded bg-slate-100 text-slate-700 border border-slate-200"
                            >
                              {p}
                            </span>
                          ))
                        ) : (
                          <span className="text-xs text-slate-400">بدون دسترسی</span>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={4} className="py-8 text-center text-slate-400 text-xs">
                    {loading ? 'در حال بارگذاری نقش‌ها...' : 'هیچ نقشی تعریف نشده است.'}
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
