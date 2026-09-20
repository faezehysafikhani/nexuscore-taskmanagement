import React, { useEffect, useState } from 'react';
import {
  CheckSquare,
  Plus,
  RefreshCw,
  User,
  AlertCircle,
  CheckCircle2,
} from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { TaskDto, OrganizationUnitDto, WorkCalendarDto, UserDto } from '../types';

interface TaskManagerProps {
  currentUser: UserDto | null;
}

const IRAN_WORK_WEEK = 125; // Nexus.Calendar.Domain.DayOfWeekMask.IranWorkWeek (Sat-Wed)

export const TaskManager: React.FC<TaskManagerProps> = ({ currentUser }) => {
  const [tasks, setTasks] = useState<TaskDto[]>([]);
  const [orgUnits, setOrgUnits] = useState<OrganizationUnitDto[]>([]);
  const [calendars, setCalendars] = useState<WorkCalendarDto[]>([]);
  const [organizationUnitId, setOrganizationUnitId] = useState('');
  const [workCalendarId, setWorkCalendarId] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [statusFilter, setStatusFilter] = useState('ALL');
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);
  const [loading, setLoading] = useState(false);
  const [settingUp, setSettingUp] = useState(false);

  useEffect(() => {
    loadAll();
  }, []);

  const loadAll = async () => {
    setLoading(true);
    const [tasksRes, unitsRes, calendarsRes] = await Promise.all([
      api.get<TaskDto[]>('/api/actions'),
      api.get<OrganizationUnitDto[]>('/api/organization/units'),
      api.get<WorkCalendarDto[]>('/api/calendar/work-calendars'),
    ]);
    setLoading(false);

    if (tasksRes.isSuccess && tasksRes.value) {
      setTasks(tasksRes.value);
    } else if (tasksRes.error) {
      setIsError(true);
      setMessage(PersianMessages.error(tasksRes.error));
    }

    if (unitsRes.isSuccess && unitsRes.value) {
      setOrgUnits(unitsRes.value);
      setOrganizationUnitId((prev) => prev || unitsRes.value![0]?.id || '');
    }

    if (calendarsRes.isSuccess && calendarsRes.value) {
      setCalendars(calendarsRes.value);
      setWorkCalendarId((prev) => prev || calendarsRes.value![0]?.id || '');
    }
  };

  // Actions require a real Organization Unit and Work Calendar to exist first - there's no
  // dedicated admin page for either yet, so create one with sensible defaults inline.
  const quickSetup = async () => {
    if (!currentUser) return;
    setSettingUp(true);
    setMessage(null);

    let unitId = organizationUnitId;
    let calendarId = workCalendarId;

    if (!unitId) {
      const res = await api.post<OrganizationUnitDto>('/api/organization/units', {
        tenantId: currentUser.tenantId,
        name: 'واحد پیش‌فرض',
        code: 'DEFAULT',
        parentId: null,
      });
      if (res.isSuccess && res.value) {
        unitId = res.value.id;
        setOrgUnits((prev) => [...prev, res.value!]);
        setOrganizationUnitId(unitId);
      } else {
        setIsError(true);
        setMessage(PersianMessages.error(res.error));
        setSettingUp(false);
        return;
      }
    }

    if (!calendarId) {
      const res = await api.post<WorkCalendarDto>('/api/calendar/work-calendars', {
        tenantId: currentUser.tenantId,
        name: 'تقویم کاری پیش‌فرض',
        workingDays: IRAN_WORK_WEEK,
        isDefault: true,
      });
      if (res.isSuccess && res.value) {
        calendarId = res.value.id;
        setCalendars((prev) => [...prev, res.value!]);
        setWorkCalendarId(calendarId);
      } else {
        setIsError(true);
        setMessage(PersianMessages.error(res.error));
        setSettingUp(false);
        return;
      }
    }

    setIsError(false);
    setMessage('واحد سازمانی و تقویم کاری پیش‌فرض ایجاد شد. اکنون می‌توانید تسک ثبت کنید.');
    setSettingUp(false);
  };

  const handleCreateTask = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title) {
      setIsError(true);
      setMessage('عنوان تسک الزامی است.');
      return;
    }
    if (!currentUser || !organizationUnitId || !workCalendarId) {
      setIsError(true);
      setMessage('ابتدا واحد سازمانی و تقویم کاری را تنظیم کنید.');
      return;
    }

    setLoading(true);
    const result = await api.post<TaskDto>('/api/actions', {
      tenantId: currentUser.tenantId,
      title,
      description: description || null,
      ownerUserId: currentUser.id,
      responsibleUserId: currentUser.id,
      organizationUnitId,
      workCalendarId,
      projectId: null,
      startDate: null,
      endDate: null,
    });
    setLoading(false);

    if (result.isSuccess) {
      setIsError(false);
      setMessage('تسک جدید با موفقیت ایجاد شد.');
      setTitle('');
      setDescription('');
      loadAll();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const handleUpdateStatus = async (task: TaskDto, newStatus: string) => {
    const result = await api.put(`/api/actions/${task.id}/status`, {
      status: newStatus,
    });

    if (result.isSuccess) {
      loadAll();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const filteredTasks = tasks.filter((t) => {
    if (statusFilter === 'ALL') return true;
    return t.status === statusFilter;
  });

  const needsSetup = orgUnits.length === 0 || calendars.length === 0;

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2 border-b border-slate-200">
        <div className="flex items-center gap-3">
          <div className="p-2.5 bg-amber-500/10 text-amber-600 rounded-xl">
            <CheckSquare className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-slate-900">مدیریت تسک‌ها و وظایف (Task Manager)</h1>
            <p className="text-xs text-slate-500">برنامه‌ریزی، تخصیص و پیگیری وظایف تیم‌های توسعه و عملیات</p>
          </div>
        </div>
        <button onClick={loadAll} disabled={loading} className="btn-secondary-nexus text-xs">
          <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          <span>به‌روزرسانی</span>
        </button>
      </div>

      {/* Alert */}
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
          <button onClick={() => setMessage(null)} className="text-slate-400 hover:text-slate-600">
            ×
          </button>
        </div>
      )}

      {/* First-run setup: Actions need a real Organization Unit + Work Calendar to exist */}
      {needsSetup && (
        <div className="p-4 rounded-xl border border-amber-200 bg-amber-50 text-amber-900 text-xs flex items-center justify-between gap-3">
          <span>
            هنوز واحد سازمانی یا تقویم کاری تعریف نشده و ثبت تسک به آن‌ها نیاز دارد.
          </span>
          <button
            onClick={quickSetup}
            disabled={settingUp}
            className="btn-primary-nexus text-xs bg-amber-600 hover:bg-amber-700 whitespace-nowrap"
          >
            {settingUp ? 'در حال ایجاد...' : 'ایجاد خودکار موارد پیش‌فرض'}
          </button>
        </div>
      )}

      {/* Create Task Panel */}
      <div className="form-panel">
        <div className="flex items-center gap-2 font-bold text-sm text-slate-800 mb-4 pb-2 border-b border-slate-100 w-full">
          <Plus className="w-4 h-4 text-amber-600" />
          <span>تعریف تسک جدید</span>
        </div>
        <form
          onSubmit={handleCreateTask}
          className="w-full grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 items-end"
        >
          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">عنوان تسک</label>
            <input
              type="text"
              required
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="مثال: به‌روزرسانی قوانین دسترسی"
              className="input-field"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">توضیحات</label>
            <input
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="توضیح مختصر در مورد کار..."
              className="input-field"
            />
          </div>

          <div>
            <label className="block text-xs font-semibold text-slate-600 mb-1">واحد سازمانی</label>
            <select
              value={organizationUnitId}
              onChange={(e) => setOrganizationUnitId(e.target.value)}
              className="input-field bg-white"
              disabled={orgUnits.length === 0}
            >
              {orgUnits.length === 0 && <option value="">— موردی موجود نیست —</option>}
              {orgUnits.map((u) => (
                <option key={u.id} value={u.id}>{u.name}</option>
              ))}
            </select>
          </div>

          <div>
            <button
              type="submit"
              disabled={loading || !organizationUnitId || !workCalendarId}
              className="w-full btn-primary-nexus py-2 text-xs bg-amber-600 hover:bg-amber-700"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>ثبت تسک</span>
            </button>
          </div>
        </form>
      </div>

      {/* Task Filters & Board List */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm p-4">
        <div className="flex flex-wrap items-center justify-between gap-3 pb-3 mb-4 border-b border-slate-100">
          <div className="font-bold text-sm text-slate-800">
            لیست تسک‌ها ({filteredTasks.length} مورد)
          </div>
          <div className="flex items-center gap-1.5 text-xs">
            <span className="text-slate-500 font-semibold ml-2">فیلتر وضعیت:</span>
            {['ALL', 'Open', 'InProgress', 'Completed', 'Cancelled'].map((status) => (
              <button
                key={status}
                onClick={() => setStatusFilter(status)}
                className={`px-3 py-1 rounded-lg font-medium transition-all ${
                  statusFilter === status
                    ? 'bg-amber-600 text-white shadow-xs'
                    : 'bg-slate-100 text-slate-600 hover:bg-slate-200'
                }`}
              >
                {status === 'ALL'
                  ? 'همه'
                  : status === 'Open'
                  ? 'برای انجام'
                  : status === 'InProgress'
                  ? 'در حال انجام'
                  : status === 'Completed'
                  ? 'انجام شد'
                  : 'لغو شده'}
              </button>
            ))}
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {filteredTasks.map((task) => (
            <div
              key={task.id}
              className="p-4 rounded-xl border border-slate-200 bg-slate-50/50 hover:bg-white hover:shadow-md transition-all flex flex-col justify-between"
            >
              <div>
                <h3 className="font-bold text-sm text-slate-900 leading-snug mb-2">{task.title}</h3>
                {task.description && (
                  <p className="text-xs text-slate-600 leading-relaxed mb-3">
                    {task.description}
                  </p>
                )}
              </div>

              <div className="pt-3 border-t border-slate-200/60 flex items-center justify-between gap-2 text-xs">
                <div className="flex items-center gap-1 text-slate-500">
                  <User className="w-3.5 h-3.5" />
                  <span>{orgUnits.find((u) => u.id === task.organizationUnitId)?.name || 'واحد نامشخص'}</span>
                </div>

                <select
                  value={task.status}
                  onChange={(e) => handleUpdateStatus(task, e.target.value)}
                  className="text-xs bg-white border border-slate-300 rounded-md px-2 py-1 focus:outline-none focus:border-blue-500 font-semibold"
                >
                  <option value="Open">برای انجام</option>
                  <option value="InProgress">در حال انجام</option>
                  <option value="Completed">انجام شد</option>
                  <option value="Cancelled">لغو شده</option>
                </select>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};
