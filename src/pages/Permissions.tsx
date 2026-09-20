import React, { useEffect, useState } from 'react';
import { Key, RefreshCw, Layers, ShieldCheck, AlertCircle } from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { PermissionGroupDto } from '../types';

export const Permissions: React.FC = () => {
  const [groups, setGroups] = useState<PermissionGroupDto[]>([]);
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadPermissions();
  }, []);

  const loadPermissions = async () => {
    setLoading(true);
    const result = await api.get<PermissionGroupDto[]>('/api/identity/permissions');
    setLoading(false);

    if (result.isSuccess && result.value) {
      setGroups(result.value);
      setIsError(false);
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
          <div className="p-2.5 bg-amber-500/10 text-amber-600 rounded-xl">
            <Key className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">دسترسی‌ها و اختیارات (Permissions)</h1>
            <p className="text-xs text-slate-500">لیست دسترسی‌های دانه‌بندی‌شده سیستم به تفکیک ماژول‌ها</p>
          </div>
        </div>
        <button
          onClick={loadPermissions}
          disabled={loading}
          className="btn-secondary-nexus text-xs"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>به‌روزرسانی دسترسی‌ها</span>
        </button>
      </div>

      {message && (
        <div className="p-3.5 rounded-lg text-xs font-medium bg-rose-50 border border-rose-200 text-rose-800 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
          <span>{message}</span>
        </div>
      )}

      {/* Grouped Modules */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {groups.map((group) => (
          <div
            key={group.module}
            className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden flex flex-col"
          >
            <div className="p-4 bg-slate-50 border-b border-slate-200 flex items-center justify-between">
              <div className="flex items-center gap-2 font-bold text-sm text-slate-800">
                <Layers className="w-4 h-4 text-amber-600" />
                <span>ماژول {group.module}</span>
              </div>
              <span className="text-xs px-2.5 py-0.5 rounded-full font-semibold bg-amber-100/60 text-amber-900 border border-amber-200">
                {group.permissions.length} دسترسی
              </span>
            </div>

            <div className="p-0 overflow-x-auto flex-1">
              <table className="table-nexus">
                <tbody>
                  {group.permissions.map((perm) => (
                    <tr key={perm.id}>
                      <td className="w-1/2">
                        <div className="flex items-center gap-2">
                          <ShieldCheck className="w-3.5 h-3.5 text-emerald-600 shrink-0" />
                          <span className="font-mono text-xs font-semibold text-slate-900">
                            {perm.name}
                          </span>
                        </div>
                      </td>
                      <td className="text-xs text-slate-600">{perm.description}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
};
