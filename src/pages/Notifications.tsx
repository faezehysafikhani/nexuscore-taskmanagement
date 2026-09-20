import React, { useEffect, useState } from 'react';
import { Bell, CheckCheck, RefreshCw, Info, AlertTriangle, CheckCircle, ShieldCheck } from 'lucide-react';
import { api } from '../services/api';
import { NotificationDto } from '../types';

interface NotificationsProps {
  onRefreshUnread: () => void;
}

export const Notifications: React.FC<NotificationsProps> = ({ onRefreshUnread }) => {
  const [notifications, setNotifications] = useState<NotificationDto[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadNotifications();
  }, []);

  const loadNotifications = async () => {
    setLoading(true);
    const res = await api.get<NotificationDto[]>('/api/notifications');
    setLoading(false);
    if (res.isSuccess && res.value) {
      setNotifications(res.value);
    }
  };

  const markAllRead = async () => {
    const res = await api.put('/api/notifications/read-all', {});
    if (res.isSuccess) {
      setNotifications((prev) => prev.map((n) => ({ ...n, isRead: true })));
      onRefreshUnread();
    }
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2 border-b border-slate-200">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-blue-600/10 text-blue-600 rounded-xl">
            <Bell className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">مرکز اعلان‌های سیستم (Notifications)</h1>
            <p className="text-xs text-slate-500">پیام‌ها و هشدارهای ارسال‌شده از ماژول‌های مختلف نکسوس</p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={markAllRead}
            className="btn-secondary-nexus text-xs"
          >
            <CheckCheck className="w-3.5 h-3.5 text-emerald-600" />
            <span>خواندن همه اعلان‌ها</span>
          </button>
          <button
            onClick={loadNotifications}
            disabled={loading}
            className="btn-secondary-nexus text-xs"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {/* Notifications List */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden divide-y divide-slate-100">
        {notifications.length > 0 ? (
          notifications.map((n) => {
            const icons: Record<string, any> = {
              Security: ShieldCheck,
              Warning: AlertTriangle,
              Success: CheckCircle,
              Info: Info,
            };
            const Icon = icons[n.type] || Info;

            const iconColors: Record<string, string> = {
              Security: 'text-purple-600 bg-purple-50',
              Warning: 'text-amber-600 bg-amber-50',
              Success: 'text-emerald-600 bg-emerald-50',
              Info: 'text-blue-600 bg-blue-50',
            };

            return (
              <div
                key={n.id}
                className={`p-4 flex items-start gap-3.5 transition-colors ${
                  !n.isRead ? 'bg-blue-50/40' : 'hover:bg-slate-50'
                }`}
              >
                <div
                  className={`w-9 h-9 rounded-xl flex items-center justify-center shrink-0 ${
                    iconColors[n.type] || 'text-slate-600 bg-slate-100'
                  }`}
                >
                  <Icon className="w-5 h-5" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center justify-between gap-2 mb-1">
                    <h3 className="font-bold text-xs text-slate-900">{n.title}</h3>
                    <span className="text-[10px] text-slate-400 font-mono whitespace-nowrap">
                      {new Date(n.createdAtUtc).toLocaleString('fa-IR')}
                    </span>
                  </div>
                  <p className="text-xs text-slate-600 leading-relaxed">{n.message}</p>
                </div>
                {!n.isRead && (
                  <span className="w-2.5 h-2.5 rounded-full bg-blue-600 shrink-0 mt-1.5" />
                )}
              </div>
            );
          })
        ) : (
          <div className="py-12 text-center text-slate-400 text-xs">
            هیچ اعلانی یافت نشد.
          </div>
        )}
      </div>
    </div>
  );
};
