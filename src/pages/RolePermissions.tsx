import React, { useState } from 'react';
import { ShieldAlert, RefreshCw, CheckCircle2, AlertCircle, Shield, Key, Check } from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { RoleDto, PermissionGroupDto } from '../types';

export const RolePermissions: React.FC = () => {
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [permissionGroups, setPermissionGroups] = useState<PermissionGroupDto[]>([]);
  const [tenantId, setTenantId] = useState('11111111-1111-1111-1111-111111111111');
  const [selectedRoleId, setSelectedRoleId] = useState('');
  const [permissionIds, setPermissionIds] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);

  const loadRolesAndPermissions = async () => {
    setLoading(true);
    const [rolesRes, permsRes] = await Promise.all([
      api.get<RoleDto[]>(`/api/identity/roles?tenantId=${tenantId}`),
      api.get<PermissionGroupDto[]>('/api/identity/permissions'),
    ]);
    setLoading(false);

    if (rolesRes.isSuccess && rolesRes.value && permsRes.isSuccess && permsRes.value) {
      setRoles(rolesRes.value);
      setPermissionGroups(permsRes.value);
      setIsError(false);
      setMessage(null);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(rolesRes.error || permsRes.error));
    }
  };

  const handleRoleSelect = (roleId: string) => {
    setSelectedRoleId(roleId);
    const role = roles.find((r) => r.id === roleId);
    if (role && permissionGroups.length > 0) {
      const allPerms = permissionGroups.flatMap((g) => g.permissions);
      // If role.permissions contains names or IDs
      const matchedIds = allPerms
        .filter((p) => role.permissions?.includes(p.name) || role.permissions?.includes(p.id))
        .map((p) => p.id);
      setPermissionIds(matchedIds.join(', '));
    }
  };

  const togglePermission = (permId: string) => {
    const currentList = permissionIds
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    let updated: string[];
    if (currentList.includes(permId)) {
      updated = currentList.filter((id) => id !== permId);
    } else {
      updated = [...currentList, permId];
    }
    setPermissionIds(updated.join(', '));
  };

  const handleAssign = async () => {
    if (!selectedRoleId) {
      setIsError(true);
      setMessage('لطفاً یک نقش را انتخاب کنید.');
      return;
    }

    const parsedPermissionIds = permissionIds
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);

    setLoading(true);
    const result = await api.put(`/api/identity/roles/${selectedRoleId}/permissions`, {
      permissionIds: parsedPermissionIds,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage('دسترسی‌ها با موفقیت به نقش اختصاص داده شدند.');
      loadRolesAndPermissions();
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
          <div className="p-2.5 bg-purple-600/10 text-purple-600 rounded-xl">
            <ShieldAlert className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">اختصاص دسترسی به نقش (Role Permissions)</h1>
            <p className="text-xs text-slate-500">پیکربندی سطوح دسترسی هر نقش بر روی ماژول‌ها و اکشن‌ها</p>
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
          onClick={loadRolesAndPermissions}
          disabled={loading}
          className="btn-secondary-nexus text-xs"
        >
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>بارگذاری نقش‌ها و دسترسی‌ها</span>
        </button>
      </div>

      {/* Assignment Workspace */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column: Role Selector & Action */}
        <div className="panel space-y-4 h-fit">
          <h2 className="font-bold text-sm text-slate-800 pb-2 border-b border-slate-100 flex items-center gap-2">
            <Shield className="w-4 h-4 text-purple-600" />
            <span>تنظیمات نقش مورد نظر</span>
          </h2>

          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">انتخاب نقش</label>
            <select
              value={selectedRoleId}
              onChange={(e) => handleRoleSelect(e.target.value)}
              className="input-field bg-white"
            >
              <option value="">-- لطفاً نقش را انتخاب نمایید --</option>
              {roles.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name} {r.isSystem ? '(سیستمی)' : ''}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1.5">
              شناسه دسترسی‌ها (Permission IDs)
            </label>
            <textarea
              rows={4}
              value={permissionIds}
              onChange={(e) => setPermissionIds(e.target.value)}
              placeholder="p-101, p-102, ..."
              className="input-field font-mono text-xs"
            />
          </div>

          <button
            onClick={handleAssign}
            disabled={loading || !selectedRoleId}
            className="w-full btn-primary-nexus py-2.5 text-xs font-semibold bg-purple-600 hover:bg-purple-700"
          >
            <ShieldAlert className="w-4 h-4" />
            <span>ذخیره دسترسی‌های نقش</span>
          </button>
        </div>

        {/* Right Columns: Interactive Permissions Checklist */}
        <div className="lg:col-span-2 space-y-4">
          <div className="bg-white rounded-xl border border-slate-200 shadow-sm p-4">
            <div className="font-bold text-sm text-slate-800 mb-3 pb-2 border-b border-slate-100 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Key className="w-4 h-4 text-amber-600" />
                <span>دسترسی‌های ماژول‌ها (برای انتخاب یا حذف کلیک کنید)</span>
              </div>
            </div>

            {permissionGroups.length > 0 ? (
              <div className="space-y-4">
                {permissionGroups.map((group) => (
                  <div key={group.module} className="border border-slate-200 rounded-lg overflow-hidden">
                    <div className="bg-slate-50 px-3 py-2 text-xs font-bold text-slate-700 border-b border-slate-200">
                      ماژول {group.module}
                    </div>
                    <div className="p-2 grid grid-cols-1 sm:grid-cols-2 gap-2">
                      {group.permissions.map((p) => {
                        const currentList = permissionIds.split(',').map((s) => s.trim());
                        const isSelected = currentList.includes(p.id);
                        return (
                          <div
                            key={p.id}
                            onClick={() => togglePermission(p.id)}
                            className={`p-2.5 rounded-lg border cursor-pointer transition-all flex items-start gap-2.5 ${
                              isSelected
                                ? 'bg-purple-50/80 border-purple-300 shadow-xs'
                                : 'bg-white border-slate-200 hover:bg-slate-50'
                            }`}
                          >
                            <div
                              className={`w-4 h-4 rounded mt-0.5 shrink-0 flex items-center justify-center border transition-colors ${
                                isSelected
                                  ? 'bg-purple-600 border-purple-600 text-white'
                                  : 'border-slate-300 bg-white'
                              }`}
                            >
                              {isSelected && <Check className="w-3 h-3" />}
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className="font-mono text-xs font-semibold text-slate-800 truncate">
                                {p.name}
                              </div>
                              <div className="text-[11px] text-slate-500 mt-0.5">
                                {p.description}
                              </div>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="text-center py-10 text-slate-400 text-xs">
                جهت مشاهده دسترسی‌ها، دکمه «بارگذاری نقش‌ها و دسترسی‌ها» را انتخاب نمایید.
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
