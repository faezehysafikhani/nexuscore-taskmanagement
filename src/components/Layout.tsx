import React, { useState } from 'react';
import {
  LayoutDashboard,
  Users,
  Shield,
  Key,
  UserCheck,
  ShieldAlert,
  Building2,
  FileText,
  CheckSquare,
  Calendar,
  MessageSquare,
  Ticket,
  Bell,
  Settings,
  LogIn,
  LogOut,
  Menu,
  X,
  Server,
  Layers,
  ChevronLeft,
} from 'lucide-react';
import { UserDto } from '../types';
import { API_BASE_URL } from '../services/api';

interface LayoutProps {
  currentPage: string;
  onNavigate: (page: string) => void;
  currentUser: UserDto | null;
  unreadNotificationsCount: number;
  onLogout: () => void;
  children: React.ReactNode;
}

export const Layout: React.FC<LayoutProps> = ({
  currentPage,
  onNavigate,
  currentUser,
  unreadNotificationsCount,
  onLogout,
  children,
}) => {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  const navItems = [
    { id: 'dashboard', label: 'داشبورد', icon: LayoutDashboard },
    { id: 'users', label: 'کاربران', icon: Users },
    { id: 'roles', label: 'نقش‌ها', icon: Shield },
    { id: 'permissions', label: 'دسترسی‌ها', icon: Key },
    { id: 'user-roles', label: 'نقش‌های کاربر', icon: UserCheck },
    { id: 'role-permissions', label: 'دسترسی‌های نقش', icon: ShieldAlert },
    { id: 'tenants', label: 'سازمان‌ها', icon: Building2 },
    { id: 'audit-logs', label: 'گزارش فعالیت‌ها', icon: FileText },
    { id: 'tasks', label: 'مدیریت تسک‌ها', icon: CheckSquare },
    { id: 'events', label: 'رویدادها و تقویم', icon: Calendar },
    { id: 'chat', label: 'پیام‌رسان', icon: MessageSquare },
    { id: 'tickets', label: 'تیکتینگ و پشتیبانی', icon: Ticket },
    { id: 'notifications', label: 'اعلان‌ها', icon: Bell, badge: unreadNotificationsCount },
    { id: 'settings', label: 'تنظیمات پلتفرم', icon: Settings },
  ];

  const handleNavClick = (pageId: string) => {
    onNavigate(pageId);
    setMobileMenuOpen(false);
  };

  return (
    <div className="min-h-screen flex flex-col bg-slate-100 text-slate-800 font-sans" dir="rtl">
      {/* Top Navigation Bar */}
      <header className="bg-slate-900 text-white sticky top-0 z-40 shadow-md border-b border-slate-800">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 h-16 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <button
              onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
              className="lg:hidden p-2 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 focus:outline-none"
              aria-label="منوی ناوبری"
            >
              {mobileMenuOpen ? <X className="w-6 h-6" /> : <Menu className="w-6 h-6" />}
            </button>
            <div
              onClick={() => handleNavClick('dashboard')}
              className="flex items-center gap-2.5 cursor-pointer select-none group"
            >
              <div className="w-9 h-9 rounded-lg bg-gradient-to-tr from-blue-600 to-indigo-500 flex items-center justify-center text-white shadow-md shadow-blue-500/20 group-hover:scale-105 transition-transform">
                <Layers className="w-5 h-5" />
              </div>
              <div>
                <span className="font-bold text-lg tracking-tight block leading-tight text-white">
                  مدیریت NexusCore
                </span>
                <span className="text-[11px] text-slate-400 font-medium block">
                  Enterprise Platform Core
                </span>
              </div>
            </div>
          </div>

          <div className="flex items-center gap-3 sm:gap-4">
            {/* Quick Status Badge */}
            <div className="hidden sm:flex items-center gap-1.5 px-3 py-1 rounded-full bg-slate-800/80 border border-slate-700/60 text-xs text-emerald-400">
              <span className="w-2 h-2 rounded-full bg-emerald-400 animate-pulse"></span>
              <span>API متصل ({API_BASE_URL.replace(/^https?:\/\//, '')})</span>
            </div>

            {/* Notifications Bell */}
            <button
              onClick={() => handleNavClick('notifications')}
              className="relative p-2 rounded-lg text-slate-300 hover:text-white hover:bg-slate-800 transition-colors"
              title="اعلان‌ها"
            >
              <Bell className="w-5 h-5" />
              {unreadNotificationsCount > 0 && (
                <span className="absolute top-1.5 right-1.5 w-4 h-4 rounded-full bg-rose-500 text-white text-[10px] font-bold flex items-center justify-center">
                  {unreadNotificationsCount}
                </span>
              )}
            </button>

            {/* User Profile / Login */}
            {currentUser ? (
              <div className="flex items-center gap-3 pr-2 border-r border-slate-800">
                <div className="hidden md:block text-left text-xs">
                  <div className="font-semibold text-slate-200">{currentUser.displayName}</div>
                  <div className="text-slate-400 text-[11px]">{currentUser.email}</div>
                </div>
                <div className="w-8 h-8 rounded-full bg-blue-600/30 border border-blue-400/40 text-blue-300 font-bold text-xs flex items-center justify-center">
                  {currentUser.displayName.charAt(0)}
                </div>
                <button
                  onClick={onLogout}
                  className="p-2 rounded-lg text-rose-400 hover:bg-rose-950/40 hover:text-rose-300 transition-colors text-xs flex items-center gap-1"
                  title="خروج از حساب"
                >
                  <LogOut className="w-4 h-4" />
                  <span className="hidden sm:inline">خروج</span>
                </button>
              </div>
            ) : (
              <button
                onClick={() => handleNavClick('login')}
                className="flex items-center gap-2 px-3.5 py-1.5 bg-blue-600 hover:bg-blue-500 text-white rounded-lg text-xs font-semibold shadow transition-all"
              >
                <LogIn className="w-4 h-4" />
                <span>ورود به سیستم</span>
              </button>
            )}
          </div>
        </div>
      </header>

      {/* Main Container with Sidebar */}
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-6 w-full flex-1 flex flex-col lg:flex-row gap-6">
        {/* Sidebar for Desktop */}
        <aside className="hidden lg:block w-64 shrink-0">
          <div className="bg-white rounded-xl border border-slate-200 shadow-sm p-3 sticky top-24">
            <div className="px-3 py-2 text-xs font-semibold text-slate-400 uppercase tracking-wider">
              منوی ناوبری اصلی
            </div>
            <nav className="space-y-1 mt-1">
              {navItems.map((item) => {
                const Icon = item.icon;
                const isActive = currentPage === item.id;
                return (
                  <button
                    key={item.id}
                    onClick={() => handleNavClick(item.id)}
                    className={`w-full flex items-center justify-between px-3 py-2.5 rounded-lg text-sm font-medium transition-all ${
                      isActive
                        ? 'bg-blue-600 text-white shadow-sm shadow-blue-500/20'
                        : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900'
                    }`}
                  >
                    <div className="flex items-center gap-3">
                      <Icon className={`w-4 h-4 ${isActive ? 'text-white' : 'text-slate-400'}`} />
                      <span>{item.label}</span>
                    </div>
                    {item.badge !== undefined && item.badge > 0 ? (
                      <span
                        className={`text-xs px-2 py-0.5 rounded-full font-bold ${
                          isActive ? 'bg-white text-blue-600' : 'bg-rose-500 text-white'
                        }`}
                      >
                        {item.badge}
                      </span>
                    ) : (
                      isActive && <ChevronLeft className="w-4 h-4 text-white/80" />
                    )}
                  </button>
                );
              })}
            </nav>
          </div>
        </aside>

        {/* Mobile Navigation Drawer */}
        {mobileMenuOpen && (
          <div className="lg:hidden fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-sm flex">
            <div className="w-72 bg-white h-full p-4 overflow-y-auto shadow-2xl flex flex-col">
              <div className="flex items-center justify-between pb-4 border-b border-slate-100 mb-3">
                <span className="font-bold text-slate-800">منوی مدیریت نکسوس</span>
                <button
                  onClick={() => setMobileMenuOpen(false)}
                  className="p-2 rounded-lg text-slate-400 hover:bg-slate-100"
                >
                  <X className="w-5 h-5" />
                </button>
              </div>
              <nav className="space-y-1 flex-1">
                {navItems.map((item) => {
                  const Icon = item.icon;
                  const isActive = currentPage === item.id;
                  return (
                    <button
                      key={item.id}
                      onClick={() => handleNavClick(item.id)}
                      className={`w-full flex items-center justify-between px-3 py-2.5 rounded-lg text-sm font-medium transition-all ${
                        isActive
                          ? 'bg-blue-600 text-white shadow-sm'
                          : 'text-slate-600 hover:bg-slate-50'
                      }`}
                    >
                      <div className="flex items-center gap-3">
                        <Icon className={`w-4 h-4 ${isActive ? 'text-white' : 'text-slate-400'}`} />
                        <span>{item.label}</span>
                      </div>
                      {item.badge !== undefined && item.badge > 0 && (
                        <span className="text-xs px-2 py-0.5 rounded-full font-bold bg-rose-500 text-white">
                          {item.badge}
                        </span>
                      )}
                    </button>
                  );
                })}
              </nav>
            </div>
            <div className="flex-1" onClick={() => setMobileMenuOpen(false)} />
          </div>
        )}

        {/* Main Content Area */}
        <main className="flex-1 min-w-0">{children}</main>
      </div>

      {/* Footer */}
      <footer className="bg-white border-t border-slate-200 mt-auto py-4 text-center text-xs text-slate-500">
        <div className="max-w-7xl mx-auto px-4 flex flex-col sm:flex-row items-center justify-between gap-2">
          <div>سامانه یکپارچه مدیریت NexusCore — نسخه وب چندمستاجره</div>
          <div className="text-slate-400">Node.js Express + React Core Engine</div>
        </div>
      </footer>
    </div>
  );
};
