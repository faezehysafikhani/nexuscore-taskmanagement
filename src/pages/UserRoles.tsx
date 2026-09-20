import React, { useState } from 'react';
import { UserCheck, RefreshCw, CheckCircle2, AlertCircle, Shield, User } from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { UserDto, RoleDto, PagedResult } from '../types';

export const UserRoles: React.FC = () => {
  const [users, setUsers] = useState<UserDto[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [tenantId, setTenantId] = useState('11111111-1111-1111-1111-111111111111');
  const [selectedUserId, setSelectedUserId] = useState('');
  const [roleIds, setRoleIds] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  const loadUsersAndRoles = async () => {
    setLoading(true);
    const [usersRes, rolesRes] = await Promise.all([
      api.get<PagedResult<UserDto>>(`/api/identity/users?tenantId=${tenantId}&pageNumber=1&pageSize=100`),
      api.get<RoleDto[]>(`/api/identity/roles?tenantId=${tenantId}`),
    ]);
    setLoading(false);

    if (usersRes.isSuccess && usersRes.value && rolesRes.isSuccess && rolesRes.value) {
      setUsers(usersRes.value.items);
      setRoles(rolesRes.value);
      setIsError(false);
      setMessage(null);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(usersRes.error || rolesRes.error));
    }
  };

  const handleUserSelect = (userId: string) => {
    setSelectedUserId(userId);
    const user = users.find((u) => u.id === userId);
    if (user) {
      // Find matching role IDs for user's role names
      const matchedRoleIds = roles
        .filter((r) => user.roles?.includes(r.name))
        .map((r) => r.id);
      setRoleIds(matchedRoleIds.join(', '));
    }
  };

  const toggleRole = (roleId: string) => {
    const currentList = roleIds
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    let updated: string[];
    if (currentList.includes(roleId)) {
      updated = currentList.filter((id) => id !== roleId);
    } else {
      updated = [...currentList, roleId];
    }
    setRoleIds(updated.join(', '));
  };

  const handleAssign = async () => {
    if (!selectedUserId) {
      setIsError(true);
      setMessage('لطفاً یک کاربر را انتخاب کنید.');
      return;
    }

    const parsedRoleIds = roleIds
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);

    setLoading(true);
    const result = await api.put(`/api/identity/users/${selectedUserId}/roles`, {
      roleIds: parsedRoleIds,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage('نقش‌ها با موفقیت به کاربر اختصاص داده شدند.');
      loadUsersAndRoles();
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
            <UserCheck className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">اختصاص نقش به کاربر (User Roles)</h1>
            <p className="text-xs text-slate-500">انتساب و تغییر نقش‌های اختصاص‌یافته به کاربران سازمان</p>
          </div>
        </div>
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

      {/* Tenant Load Panel */}
      <div className="form-panel flex flex-col sm:flex-row items-stretch sm:items-end gap-3">
        <div className="flex-1">
          <label className="block text-xs font-semibold text-slate-600 mb-1">شناسه سازمان</label>
          <input
            type="text"
            value={tenantId}
            onChange={(e) => setTenantId(e.target.value)}
            placeholder="Tenant ID"
            className="input-field font-mono text-xs"
          />
        </div>
        <button
          onClick={loadUsersAndRoles}
          disabled={loading}
          className="btn-secondary-nexus text-xs"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>بارگذاری کاربران و نقش‌ها</span>
        </button>
      </div>

      {/* Assignment Workspace */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div className="panel space-y-4">
          <h2 className="font-bold text-sm text-slate-800 pb-2 border-b border-slate-100 flex items-center gap-2">
            <User className="w-4 h-4 text-blue-600" />
            <span>انتخاب کاربر و اعمال تغییرات</span>
          </h2>

          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">انتخاب کاربر</label>
            <select
              value={selectedUserId}
              onChange={(e) => handleUserSelect(e.target.value)}
              className="input-field bg-white"
            >
              <option value="">-- لطفاً کاربر را انتخاب نمایید --</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName} ({u.email})
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">
              شناسه نقش‌ها (جداشده با ویرگول)
            </label>
            <input
              type="text"
              value={roleIds}
              onChange={(e) => setRoleIds(e.target.value)}
              placeholder="مثال: 22222222-2222-2222-2222-222222222222"
              className="input-field font-mono text-xs"
            />
            <p className="text-[11px] text-slate-400 mt-1">
              می‌توانید مستقیماً از لیست سمت چپ با کلیک روی هر نقش آن را انتخاب کنید.
            </p>
          </div>

          <button
            onClick={handleAssign}
            disabled={loading || !selectedUserId}
            className="w-full btn-primary-nexus py-2.5 text-xs font-semibold mt-2"
          >
            <UserCheck className="w-4 h-4" />
            <span>اختصاص نقش‌ها به کاربر</span>
          </button>
        </div>

        <div className="panel space-y-3">
          <h2 className="font-bold text-sm text-slate-800 pb-2 border-b border-slate-100 flex items-center gap-2">
            <Shield className="w-4 h-4 text-indigo-600" />
            <span>نقش‌های موجود در سازمان (برای انتخاب سریع کلیک کنید)</span>
          </h2>

          {roles.length > 0 ? (
            <div className="space-y-2">
              {roles.map((role) => {
                const currentList = roleIds.split(',').map((s) => s.trim());
                const isSelected = currentList.includes(role.id);
                return (
                  <div
                    key={role.id}
                    onClick={() => toggleRole(role.id)}
                    className={`p-3 rounded-lg border cursor-pointer transition-all flex items-center justify-between ${
                      isSelected
                        ? 'bg-blue-50/80 border-blue-300 shadow-xs'
                        : 'bg-slate-50 border-slate-200 hover:bg-slate-100'
                    }`}
                  >
                    <div>
                      <div className="font-semibold text-xs text-slate-900">{role.name}</div>
                      <div className="text-[11px] text-slate-500 font-mono mt-0.5">{role.id}</div>
                    </div>
                    <span
                      className={`text-xs px-2 py-0.5 rounded font-semibold ${
                        isSelected
                          ? 'bg-blue-600 text-white'
                          : 'bg-slate-200 text-slate-600'
                      }`}
                    >
                      {isSelected ? 'انتخاب شده' : 'افزودن'}
                    </span>
                  </div>
                );
              })}
            </div>
          ) : (
            <div className="text-center py-8 text-slate-400 text-xs">
              ابتدا دکمه «بارگذاری کاربران و نقش‌ها» را بزنید.
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
