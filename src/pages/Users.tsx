import React, { useEffect, useState } from 'react';
import {
  Users as UsersIcon,
  UserPlus,
  RefreshCw,
  Save,
  CheckCircle2,
  AlertCircle,
  Search,
  Shield,
  Key,
  Trash2,
  Lock,
} from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { UserDto, PagedResult } from '../types';

export const Users: React.FC = () => {
  const [users, setUsers] = useState<UserDto[]>([]);
  const [editedNames, setEditedNames] = useState<Record<string, string>>({});
  const [tenantId, setTenantId] = useState('11111111-1111-1111-1111-111111111111');
  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('Password@123');
  const [search, setSearch] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    loadUsers();
  }, []);

  const loadUsers = async () => {
    setLoading(true);
    const query = `/api/identity/users?tenantId=${tenantId}&pageNumber=1&pageSize=50${
      search ? `&search=${encodeURIComponent(search)}` : ''
    }`;
    const result = await api.get<PagedResult<UserDto>>(query);
    setLoading(false);

    if (result.isSuccess && result.value) {
      setUsers(result.value.items);
      const names: Record<string, string> = {};
      result.value.items.forEach((u) => {
        names[u.id] = u.displayName;
      });
      setEditedNames(names);
      setIsError(false);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleCreateUser = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !displayName) {
      setIsError(true);
      setMessage('ایمیل و نام نمایشی کاربر الزامی است.');
      return;
    }

    setLoading(true);
    const result = await api.post<UserDto>('/api/identity/users', {
      tenantId,
      email,
      displayName,
      password,
      isActive: true,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage('کاربر با موفقیت در سامانه ایجاد شد.');
      setEmail('');
      setDisplayName('');
      loadUsers();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleSaveUser = async (user: UserDto) => {
    const newName = editedNames[user.id] || user.displayName;
    setLoading(true);
    const result = await api.put<UserDto>(`/api/identity/users/${user.id}`, {
      displayName: newName,
      isActive: user.isActive,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage(`اطلاعات کاربر ${newName} با موفقیت ذخیره شد.`);
      loadUsers();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleDeleteUser = async (user: UserDto) => {
    if (user.email === 'admin@nexus.local' || user.roles.includes('SuperAdmin') || user.roles.includes('Administrator')) {
      setIsError(true);
      setMessage('امکان حذف ادمین اصلی سامانه وجود ندارد (این حساب در دیتابیس سیستمی و غیرقابل حذف است).');
      return;
    }

    if (!confirm(`آیا از حذف کاربر "${user.displayName}" اطمینان دارید؟`)) return;

    setLoading(true);
    const result = await api.delete(`/api/identity/users/${user.id}`);
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage(`کاربر ${user.displayName} با موفقیت حذف شد.`);
      loadUsers();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleToggleActive = async (user: UserDto) => {
    const updatedStatus = !user.isActive;
    const result = await api.put<UserDto>(`/api/identity/users/${user.id}`, {
      displayName: user.displayName,
      isActive: updatedStatus,
    });

    if (result.isSuccess) {
      setIsError(false);
      setMessage(`وضعیت حساب کاربری به ${updatedStatus ? 'فعال' : 'غیرفعال'} تغییر یافت.`);
      loadUsers();
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
          <div className="p-2.5 bg-blue-600/10 text-blue-600 rounded-xl">
            <UsersIcon className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">مدیریت کاربران</h1>
            <p className="text-xs text-slate-500">تعریف، ویرایش و مدیریت وضعیت کاربران سازمان‌ها</p>
          </div>
        </div>
        <button
          onClick={loadUsers}
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

      {/* Create User Form Panel */}
      <div className="form-panel">
        <div className="flex items-center gap-2 font-bold text-sm text-slate-800 mb-4 pb-2 border-b border-slate-100 w-full">
          <UserPlus className="w-4 h-4 text-blue-600" />
          <span>افزودن کاربر جدید به سازمان</span>
        </div>
        <form onSubmit={handleCreateUser} className="w-full grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-3 items-end">
          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">شناسه سازمان (Tenant ID)</label>
            <input
              type="text"
              value={tenantId}
              onChange={(e) => setTenantId(e.target.value)}
              placeholder="Tenant ID"
              className="input-field font-mono text-xs"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">آدرس ایمیل</label>
            <input
              type="email"
              required
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="user@nexus.local"
              className="input-field"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">نام نمایشی</label>
            <input
              type="text"
              required
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder="مثال: علی محمدی"
              className="input-field"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">رمز عبور</label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder="Password@123"
              className="input-field"
            />
          </div>

          <div>
            <button
              type="submit"
              disabled={loading}
              className="w-full btn-primary-nexus py-2 text-xs"
            >
              <UserPlus className="w-3.5 h-3.5" />
              <span>ایجاد کاربر</span>
            </button>
          </div>
        </form>
      </div>

      {/* Users Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="p-4 border-b border-slate-100 flex flex-col sm:flex-row items-center justify-between gap-3">
          <div className="text-sm font-bold text-slate-800">
            فهرست کاربران ({users.length} کاربر)
          </div>
          <div className="relative w-full sm:w-64">
            <Search className="w-4 h-4 text-slate-400 absolute right-3 top-2.5" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && loadUsers()}
              placeholder="جستجو بر اساس نام یا ایمیل..."
              className="input-field pr-9 py-1.5 text-xs"
            />
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="table-nexus">
            <thead>
              <tr>
                <th>ایمیل کاربر</th>
                <th>نام نمایشی (قابل ویرایش)</th>
                <th>شناسه سازمان</th>
                <th>وضعیت</th>
                <th>نقش‌های منتسب</th>
                <th className="text-center">عملیات</th>
              </tr>
            </thead>
            <tbody>
              {users.length > 0 ? (
                users.map((user) => (
                  <tr key={user.id}>
                    <td className="font-mono text-xs text-slate-800">{user.email}</td>
                    <td>
                      <input
                        type="text"
                        value={editedNames[user.id] ?? user.displayName}
                        onChange={(e) =>
                          setEditedNames({ ...editedNames, [user.id]: e.target.value })
                        }
                        className="px-2.5 py-1 text-xs border border-slate-200 rounded-md focus:border-blue-500 focus:outline-none w-full max-w-[200px]"
                      />
                    </td>
                    <td className="text-xs font-mono text-slate-500 truncate max-w-[140px]" title={user.tenantId}>
                      {user.tenantId}
                    </td>
                    <td>
                      <button
                        onClick={() => handleToggleActive(user)}
                        className={`px-2 py-0.5 rounded-full text-xs font-semibold border ${
                          user.isActive
                            ? 'bg-emerald-50 text-emerald-700 border-emerald-200'
                            : 'bg-slate-100 text-slate-500 border-slate-300'
                        }`}
                      >
                        {user.isActive ? 'فعال' : 'غیرفعال'}
                      </button>
                    </td>
                    <td>
                      <div className="flex flex-wrap gap-1">
                        {user.roles && user.roles.length > 0 ? (
                          user.roles.map((r, i) => (
                            <span
                              key={i}
                              className="px-2 py-0.5 text-[11px] rounded bg-blue-50 text-blue-700 border border-blue-200"
                            >
                              {r}
                            </span>
                          ))
                        ) : (
                          <span className="text-xs text-slate-400">بدون نقش</span>
                        )}
                      </div>
                    </td>
                    <td className="text-center">
                      <div className="inline-flex items-center gap-1.5">
                        <button
                          onClick={() => handleSaveUser(user)}
                          className="px-2.5 py-1 bg-slate-50 hover:bg-blue-50 text-blue-700 border border-blue-200 rounded-lg text-xs font-semibold transition-all inline-flex items-center gap-1"
                          title="ذخیره نام"
                        >
                          <Save className="w-3.5 h-3.5" />
                          <span>ذخیره</span>
                        </button>
                        {user.email === 'admin@nexus.local' || user.roles.includes('SuperAdmin') ? (
                          <span
                            className="px-2 py-1 bg-amber-50 text-amber-800 border border-amber-200 rounded-lg text-[11px] font-semibold inline-flex items-center gap-1"
                            title="ادمین اصلی سیستم غیرقابل حذف می‌باشد"
                          >
                            <Lock className="w-3 h-3 text-amber-600" />
                            <span>ادمین غیرقابل حذف</span>
                          </span>
                        ) : (
                          <button
                            onClick={() => handleDeleteUser(user)}
                            className="px-2 py-1 bg-rose-50 hover:bg-rose-100 text-rose-700 border border-rose-200 rounded-lg text-xs font-semibold transition-all inline-flex items-center gap-1"
                            title="حذف کاربر"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                            <span>حذف</span>
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))
              ) : (
                <tr>
                  <td colSpan={6} className="py-8 text-center text-slate-400 text-xs">
                    {loading ? 'در حال بارگذاری کاربران...' : 'کاربری با این مشخصات یافت نشد.'}
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
