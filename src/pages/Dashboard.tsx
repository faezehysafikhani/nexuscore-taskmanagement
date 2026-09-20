import React, { useEffect, useState } from 'react';
import {
  Users,
  Shield,
  Building2,
  FileText,
  CheckSquare,
  Ticket,
  MessageSquare,
  Activity,
  ArrowUpRight,
  Server,
  Zap,
  Clock,
  ChevronLeft,
  Calendar,
  Bell,
} from 'lucide-react';
import { api } from '../services/api';
import { UserDto, RoleDto, TenantDto, AuditLogDto, EventDto } from '../types';

interface DashboardProps {
  onNavigate: (page: string) => void;
}

export const Dashboard: React.FC<DashboardProps> = ({ onNavigate }) => {
  const [stats, setStats] = useState({
    usersCount: 0,
    rolesCount: 0,
    tenantsCount: 0,
    auditLogsCount: 0,
    tasksCount: 0,
    ticketsCount: 0,
    eventsCount: 0,
  });
  const [recentLogs, setRecentLogs] = useState<AuditLogDto[]>([]);
  const [upcomingEvents, setUpcomingEvents] = useState<EventDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    loadDashboardData();
  }, []);

  const loadDashboardData = async () => {
    setLoading(true);
    try {
      const [usersRes, rolesRes, tenantsRes, logsRes, tasksRes, ticketsRes, eventsRes] = await Promise.all([
        api.get('/api/identity/users?pageNumber=1&pageSize=1'),
        api.get('/api/identity/roles'),
        api.get('/api/platform/tenants'),
        api.get('/api/platform/audit-logs?pageNumber=1&pageSize=5'),
        api.get('/api/actions'),
        api.get('/api/tickets/my'),
        api.get('/api/events'),
      ]);

      setStats({
        usersCount: usersRes.value?.totalCount ?? (Array.isArray(usersRes.value?.items) ? usersRes.value.items.length : 0),
        rolesCount: Array.isArray(rolesRes.value) ? rolesRes.value.length : 0,
        tenantsCount: Array.isArray(tenantsRes.value) ? tenantsRes.value.length : 0,
        auditLogsCount: logsRes.value?.totalCount ?? (Array.isArray(logsRes.value?.items) ? logsRes.value.items.length : 0),
        tasksCount: Array.isArray(tasksRes.value) ? tasksRes.value.length : 0,
        ticketsCount: Array.isArray(ticketsRes.value) ? ticketsRes.value.length : 0,
        eventsCount: Array.isArray(eventsRes.value) ? eventsRes.value.length : 0,
      });

      if (logsRes.value?.items) {
        setRecentLogs(logsRes.value.items);
      }
      if (Array.isArray(eventsRes.value)) {
        setUpcomingEvents(eventsRes.value.slice(0, 4));
      }
    } catch (e) {
      console.error(e);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-6">
      {/* Welcome Banner */}
      <div className="bg-gradient-to-l from-slate-900 via-slate-800 to-indigo-950 rounded-2xl p-6 sm:p-8 text-white shadow-lg relative overflow-hidden">
        <div className="relative z-10 max-w-2xl">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-blue-500/20 text-blue-300 text-xs font-semibold mb-3 border border-blue-500/30">
            <Zap className="w-3.5 h-3.5" />
            <span>نظام جامع احراز هویت و مدیریت چندمستاجره</span>
          </div>
          <h1 className="text-2xl sm:text-3xl font-extrabold tracking-tight mb-2">
            داشبورد مدیریت پلتفرم NexusCore
          </h1>
          <p className="text-slate-300 text-sm leading-relaxed mb-6">
            سامانه نکسوس‌کور زیرساخت مقیاس‌پذیر برای هویت، نقش‌ها، سطوح دسترسی، مدیریت کارها،
            رویدادها و تقویم، پشتیبانی و ارتباطات چندسازمانی را فراهم می‌کند.
          </p>
          <div className="flex flex-wrap gap-3">
            <button
              onClick={() => onNavigate('events')}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-sm font-semibold transition-all shadow-md flex items-center gap-2"
            >
              <Calendar className="w-4 h-4" />
              <span>تقویم و رویدادها</span>
            </button>
            <button
              onClick={() => onNavigate('users')}
              className="px-4 py-2 bg-slate-800 hover:bg-slate-700 text-slate-200 border border-slate-700 rounded-lg text-sm font-semibold transition-all flex items-center gap-2"
            >
              <Users className="w-4 h-4" />
              <span>مدیریت کاربران</span>
            </button>
          </div>
        </div>
        <div className="absolute left-4 bottom-0 opacity-10 pointer-events-none hidden md:block">
          <Server className="w-72 h-72 text-white" />
        </div>
      </div>

      {/* Metrics Grid */}
      <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-7 gap-3.5">
        {[
          { label: 'کاربران فعال', value: stats.usersCount, icon: Users, page: 'users', color: 'text-blue-600 bg-blue-50 border-blue-100' },
          { label: 'نقش‌ها', value: stats.rolesCount, icon: Shield, page: 'roles', color: 'text-indigo-600 bg-indigo-50 border-indigo-100' },
          { label: 'سازمان‌ها', value: stats.tenantsCount, icon: Building2, page: 'tenants', color: 'text-violet-600 bg-violet-50 border-violet-100' },
          { label: 'رویدادهای تقویم', value: stats.eventsCount, icon: Calendar, page: 'events', color: 'text-cyan-600 bg-cyan-50 border-cyan-100' },
          { label: 'تسک‌های جاری', value: stats.tasksCount, icon: CheckSquare, page: 'tasks', color: 'text-amber-600 bg-amber-50 border-amber-100' },
          { label: 'تیکت‌های پشتیبانی', value: stats.ticketsCount, icon: Ticket, page: 'tickets', color: 'text-rose-600 bg-rose-50 border-rose-100' },
          { label: 'گزارش فعالیت‌ها', value: stats.auditLogsCount, icon: FileText, page: 'audit-logs', color: 'text-emerald-600 bg-emerald-50 border-emerald-100' },
        ].map((m, idx) => {
          const Icon = m.icon;
          return (
            <button
              key={idx}
              onClick={() => onNavigate(m.page)}
              className="bg-white p-3.5 rounded-xl border border-slate-200 shadow-sm text-right hover:border-blue-400 hover:shadow-md transition-all group flex flex-col justify-between"
            >
              <div className="flex items-center justify-between w-full mb-2">
                <div className={`p-2 rounded-lg border ${m.color}`}>
                  <Icon className="w-4 h-4" />
                </div>
                <ArrowUpRight className="w-3.5 h-3.5 text-slate-300 group-hover:text-blue-600 transition-colors" />
              </div>
              <div>
                <div className="text-xl font-bold text-slate-900 group-hover:text-blue-600 transition-colors">
                  {loading ? '...' : m.value}
                </div>
                <div className="text-[11px] text-slate-500 font-medium mt-0.5">{m.label}</div>
              </div>
            </button>
          );
        })}
      </div>

      {/* Upcoming Events and Architecture Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-5">
        {/* Upcoming Events Preview Widget */}
        <div className="bg-white rounded-xl border border-slate-200 p-5 shadow-sm flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between mb-3">
              <div className="flex items-center gap-2 font-bold text-slate-800 text-sm">
                <Calendar className="w-4 h-4 text-blue-600" />
                <span>رویدادهای تقویم و برنامه‌ها</span>
              </div>
              <button
                onClick={() => onNavigate('events')}
                className="text-xs font-semibold text-blue-600 hover:text-blue-800 flex items-center gap-0.5"
              >
                <span>تقویم کامل</span>
                <ChevronLeft className="w-3 h-3" />
              </button>
            </div>

            <div className="space-y-2">
              {upcomingEvents.length > 0 ? (
                upcomingEvents.map((ev) => (
                  <div
                    key={ev.id}
                    onClick={() => onNavigate('events')}
                    className="p-2.5 rounded-lg border border-slate-100 bg-slate-50 hover:bg-blue-50/50 hover:border-blue-200 transition-all cursor-pointer flex items-center justify-between gap-2 text-xs"
                  >
                    <div className="truncate">
                      <div className="font-semibold text-slate-800 truncate">{ev.title}</div>
                      <div className="text-[11px] text-slate-500 flex items-center gap-1 mt-0.5">
                        <Clock className="w-3 h-3 text-slate-400" />
                        <span>{new Date(ev.startAtUtc).toLocaleTimeString('fa-IR', { hour: '2-digit', minute: '2-digit' })}</span>
                        <span>•</span>
                        <span>{new Date(ev.startAtUtc).toLocaleDateString('fa-IR')}</span>
                      </div>
                    </div>
                    {ev.reminderMinutesBefore && (
                      <span className="p-1 rounded bg-amber-100 text-amber-700 shrink-0" title="یادآوری فعال">
                        <Bell className="w-3 h-3" />
                      </span>
                    )}
                  </div>
                ))
              ) : (
                <div className="text-center py-6 text-slate-400 text-xs">
                  هیچ رویدادی در تقویم ثبت نشده است.
                </div>
              )}
            </div>
          </div>

          <div className="pt-3 mt-3 border-t border-slate-100">
            <button
              onClick={() => onNavigate('events')}
              className="w-full py-2 bg-blue-50 hover:bg-blue-100 text-blue-700 rounded-lg text-xs font-semibold transition-colors flex items-center justify-center gap-1.5"
            >
              <Calendar className="w-3.5 h-3.5" />
              <span>ثبت رویداد جدید در تقویم</span>
            </button>
          </div>
        </div>

        {/* Identity & RBAC Panel */}
        <div className="panel flex flex-col justify-between">
          <div>
            <div className="flex items-center gap-2.5 mb-2 text-blue-700">
              <Shield className="w-5 h-5" />
              <h2 className="font-bold text-base text-slate-900">هویت و دسترسی (Identity & RBAC)</h2>
            </div>
            <p className="text-slate-600 text-xs leading-relaxed mb-4">
              کاربران، نقش‌ها و دسترسی‌های متنی از طریق API مدیریت می‌شوند. احراز هویت متمرکز و کنترل دسترسی بر مبنای توکن‌های استاندارد فعال است.
            </p>
          </div>
          <div className="pt-3 border-t border-slate-100 flex gap-2">
            <button
              onClick={() => onNavigate('users')}
              className="text-xs font-semibold text-blue-600 hover:text-blue-800 flex items-center gap-1"
            >
              <span>مشاهده کاربران</span>
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
            <span className="text-slate-300">|</span>
            <button
              onClick={() => onNavigate('roles')}
              className="text-xs font-semibold text-blue-600 hover:text-blue-800 flex items-center gap-1"
            >
              <span>مشاهده نقش‌ها</span>
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        {/* Platform Multi-Tenancy */}
        <div className="panel flex flex-col justify-between">
          <div>
            <div className="flex items-center gap-2.5 mb-2 text-emerald-700">
              <Building2 className="w-5 h-5" />
              <h2 className="font-bold text-base text-slate-900">زیرساخت پلتفرم (Multi-Tenancy)</h2>
            </div>
            <p className="text-slate-600 text-xs leading-relaxed mb-4">
              پایه‌های سازمان، گزارش فعالیت‌ها، رهگیری امنیتی تغییرات و تنظیمات برای ماژول‌های توسعه‌یافته با بالاترین امنیت آماده است.
            </p>
          </div>
          <div className="pt-3 border-t border-slate-100 flex gap-2">
            <button
              onClick={() => onNavigate('tenants')}
              className="text-xs font-semibold text-emerald-600 hover:text-emerald-800 flex items-center gap-1"
            >
              <span>مدیریت سازمان‌ها</span>
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
            <span className="text-slate-300">|</span>
            <button
              onClick={() => onNavigate('audit-logs')}
              className="text-xs font-semibold text-emerald-600 hover:text-emerald-800 flex items-center gap-1"
            >
              <span>مشاهده لاگ‌ها</span>
              <ChevronLeft className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>

      {/* Recent Activity Log Preview */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 sm:p-5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2 font-bold text-slate-800 text-base">
            <Clock className="w-4 h-4 text-blue-600" />
            <span>آخرین فعالیت‌های ثبت‌شده در سامانه</span>
          </div>
          <button
            onClick={() => onNavigate('audit-logs')}
            className="text-xs font-semibold text-blue-600 hover:text-blue-800 flex items-center gap-1"
          >
            <span>مشاهده همه لاگ‌ها</span>
            <ChevronLeft className="w-3.5 h-3.5" />
          </button>
        </div>
        <div className="overflow-x-auto">
          <table className="table-nexus">
            <thead>
              <tr>
                <th>زمان</th>
                <th>عملیات</th>
                <th>موجودیت</th>
                <th>جزئیات</th>
                <th>آدرس IP</th>
              </tr>
            </thead>
            <tbody>
              {recentLogs.length > 0 ? (
                recentLogs.map((log) => (
                  <tr key={log.id}>
                    <td className="text-xs font-mono text-slate-500">
                      {new Date(log.occurredAtUtc).toLocaleString('fa-IR')}
                    </td>
                    <td>
                      <span className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold bg-slate-100 text-slate-800 border border-slate-200">
                        {log.action}
                      </span>
                    </td>
                    <td className="text-xs text-slate-600">
                      {log.entityName || '-'} / {log.entityId || '-'}
                    </td>
                    <td className="text-xs text-slate-700">{log.details || '-'}</td>
                    <td className="text-xs font-mono text-slate-500">{log.ipAddress || '127.0.0.1'}</td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={5} className="py-6 text-center text-slate-400 text-xs">
                    هیچ فعالیتی هنوز ثبت نشده است.
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
