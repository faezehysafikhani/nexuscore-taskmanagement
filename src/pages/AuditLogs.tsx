import React, { useEffect, useState } from 'react';
import { FileText, RefreshCw, Filter, AlertCircle, ShieldAlert, Clock, Laptop } from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { AuditLogDto, PagedResult } from '../types';

export const AuditLogs: React.FC = () => {
  const [logs, setLogs] = useState<AuditLogDto[]>([]);
  const [tenantId, setTenantId] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadAuditLogs();
  }, []);

  const loadAuditLogs = async () => {
    setLoading(true);
    const query = tenantId.trim()
      ? `/api/platform/audit-logs?tenantId=${tenantId.trim()}&pageNumber=1&pageSize=50`
      : '/api/platform/audit-logs?pageNumber=1&pageSize=50';

    const result = await api.get<PagedResult<AuditLogDto>>(query);
    setLoading(false);

    if (result.isSuccess && result.value) {
      setLogs(result.value.items);
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
          <div className="p-2.5 bg-emerald-600/10 text-emerald-600 rounded-xl">
            <FileText className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">گزارش فعالیت‌ها و ردپای امنیتی (Audit Logs)</h1>
            <p className="text-xs text-slate-500">رهگیری کلیه رخدادها، لاگین‌ها، تغییرات ساختاری و امنیتی سامانه</p>
          </div>
        </div>
        <button
          onClick={loadAuditLogs}
          disabled={loading}
          className="btn-secondary-nexus text-xs"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>به‌روزرسانی لاگ‌ها</span>
        </button>
      </div>

      {message && (
        <div className="p-3.5 rounded-lg text-xs font-medium bg-rose-50 border border-rose-200 text-rose-800 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 text-rose-600 shrink-0" />
          <span>{message}</span>
        </div>
      )}

      {/* Filter Form Panel */}
      <div className="form-panel flex flex-col sm:flex-row items-stretch sm:items-end gap-3">
        <div className="flex-1">
          <label className="block text-xs font-semibold text-slate-600 mb-1">
            فیلتر بر اساس شناسه سازمان (اختیاری)
          </label>
          <input
            type="text"
            value={tenantId}
            onChange={(e) => setTenantId(e.target.value)}
            placeholder="مثال: 11111111-1111-1111-1111-111111111111"
            className="input-field font-mono text-xs"
          />
        </div>
        <button
          onClick={loadAuditLogs}
          disabled={loading}
          className="btn-primary-nexus text-xs bg-emerald-600 hover:bg-emerald-700"
        >
          <Filter className="w-3.5 h-3.5" />
          <span>اعمال فیلتر</span>
        </button>
      </div>

      {/* Audit Logs Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-slate-100 font-bold text-sm text-slate-800 flex items-center justify-between">
          <span>رویدادهای ثبت‌شده ({logs.length} رویداد)</span>
          <span className="text-xs text-slate-400 font-normal">مرتب‌شده به ترتیب جدیدترین</span>
        </div>
        <div className="overflow-x-auto">
          <table className="table-nexus">
            <thead>
              <tr>
                <th>زمان رخداد</th>
                <th>نوع عملیات</th>
                <th>موجودیت هدف</th>
                <th>شناسه کاربر</th>
                <th>شرح جزئیات رخداد</th>
                <th>آدرس IP</th>
              </tr>
            </thead>
            <tbody>
              {logs.length > 0 ? (
                logs.map((log) => (
                  <tr key={log.id}>
                    <td className="text-xs font-mono text-slate-600 whitespace-nowrap">
                      {new Date(log.occurredAtUtc).toLocaleString('fa-IR')}
                    </td>
                    <td>
                      <span className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold bg-slate-100 text-slate-800 border border-slate-200">
                        {log.action}
                      </span>
                    </td>
                    <td className="text-xs text-slate-700 whitespace-nowrap">
                      {log.entityName ? (
                        <span className="font-semibold">{log.entityName}</span>
                      ) : (
                        '-'
                      )}{' '}
                      {log.entityId && (
                        <span className="text-slate-400 font-mono text-[11px]">
                          ({log.entityId.slice(0, 8)}...)
                        </span>
                      )}
                    </td>
                    <td className="font-mono text-xs text-slate-500 whitespace-nowrap">
                      {log.userId ? log.userId.slice(0, 8) + '...' : 'System'}
                    </td>
                    <td className="text-xs text-slate-700 max-w-md">{log.details || '-'}</td>
                    <td className="font-mono text-xs text-slate-500 whitespace-nowrap">
                      {log.ipAddress || '127.0.0.1'}
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={6} className="py-8 text-center text-slate-400 text-xs">
                    {loading ? 'در حال بارگذاری لاگ‌ها...' : 'هیچ رخدادی ثبت نشده است.'}
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
