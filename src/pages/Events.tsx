import React, { useEffect, useState } from 'react';
import {
  Calendar as CalendarIcon,
  Plus,
  RefreshCw,
  Clock,
  Bell,
  CheckCircle2,
  Circle,
  AlertCircle,
  Search,
  Trash2,
  Edit3,
  ChevronRight,
  ChevronLeft,
  CalendarDays,
  ListFilter,
  Tag,
  Check,
  X,
  Sparkles,
} from 'lucide-react';
import { api, PersianMessages } from '../services/api';
import { EventDto, CreateEventRequest, UpdateEventRequest } from '../types';

export const Events: React.FC = () => {
  const [events, setEvents] = useState<EventDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [isError, setIsError] = useState(false);

  // Filters & View State
  const [viewMode, setViewMode] = useState<'calendar' | 'agenda'>('calendar');
  const [filterStatus, setFilterStatus] = useState<'all' | 'pending' | 'completed'>('all');
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedDate, setSelectedDate] = useState<Date>(new Date());

  // Modal State
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingEvent, setEditingEvent] = useState<EventDto | null>(null);
  
  // Form State
  const [formTitle, setFormTitle] = useState('');
  const [formDescription, setFormDescription] = useState('');
  const [formStartDate, setFormStartDate] = useState('');
  const [formStartTime, setFormStartTime] = useState('09:00');
  const [formEndDate, setFormEndDate] = useState('');
  const [formEndTime, setFormEndTime] = useState('10:00');
  const [formReminder, setFormReminder] = useState<number | null>(15);

  useEffect(() => {
    loadEvents();
  }, []);

  const loadEvents = async () => {
    setLoading(true);
    const result = await api.get<EventDto[]>('/api/events');
    setLoading(false);

    if (result.isSuccess && result.value) {
      setEvents(result.value);
      setIsError(false);
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(result.error));
    }
  };

  const openCreateModal = (presetDate?: Date) => {
    setEditingEvent(null);
    setFormTitle('');
    setFormDescription('');

    const targetDate = presetDate || new Date();
    const dateStr = targetDate.toISOString().split('T')[0];
    setFormStartDate(dateStr);
    setFormStartTime('09:00');
    setFormEndDate(dateStr);
    setFormEndTime('10:00');
    setFormReminder(15);
    setIsModalOpen(true);
  };

  const openEditModal = (ev: EventDto) => {
    setEditingEvent(ev);
    setFormTitle(ev.title);
    setFormDescription(ev.description || '');

    const start = new Date(ev.startAtUtc);
    setFormStartDate(start.toISOString().split('T')[0]);
    setFormStartTime(start.toTimeString().substring(0, 5));

    if (ev.endAtUtc) {
      const end = new Date(ev.endAtUtc);
      setFormEndDate(end.toISOString().split('T')[0]);
      setFormEndTime(end.toTimeString().substring(0, 5));
    } else {
      setFormEndDate('');
      setFormEndTime('');
    }

    setFormReminder(ev.reminderMinutesBefore ?? null);
    setIsModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formTitle.trim()) {
      setIsError(true);
      setMessage('عنوان رویداد الزامی است.');
      return;
    }

    if (!formStartDate) {
      setIsError(true);
      setMessage('تاریخ شروع رویداد را مشخص کنید.');
      return;
    }

    const startDateTimeIso = new Date(`${formStartDate}T${formStartTime || '00:00'}:00`).toISOString();
    let endDateTimeIso: string | undefined = undefined;
    if (formEndDate && formEndTime) {
      endDateTimeIso = new Date(`${formEndDate}T${formEndTime}:00`).toISOString();
    }

    setLoading(true);

    if (editingEvent) {
      const payload: UpdateEventRequest = {
        title: formTitle.trim(),
        description: formDescription.trim() || undefined,
        startAtUtc: startDateTimeIso,
        endAtUtc: endDateTimeIso,
        reminderMinutesBefore: formReminder,
      };

      const res = await api.put<EventDto>(`/api/events/${editingEvent.id}`, payload);
      setLoading(false);

      if (res.isSuccess) {
        setMessage('رویداد با موفقیت ویرایش شد.');
        setIsError(false);
        setIsModalOpen(false);
        loadEvents();
      } else {
        setIsError(true);
        setMessage(PersianMessages.error(res.error));
      }
    } else {
      const payload: CreateEventRequest = {
        title: formTitle.trim(),
        description: formDescription.trim() || undefined,
        startAtUtc: startDateTimeIso,
        endAtUtc: endDateTimeIso,
        reminderMinutesBefore: formReminder,
      };

      const res = await api.post<EventDto>('/api/events', payload);
      setLoading(false);

      if (res.isSuccess) {
        setMessage('رویداد با موفقیت ثبت شد.');
        setIsError(false);
        setIsModalOpen(false);
        loadEvents();
      } else {
        setIsError(true);
        setMessage(PersianMessages.error(res.error));
      }
    }
  };

  const toggleEventCompleted = async (ev: EventDto) => {
    const newStatus = !ev.isCompleted;
    // Optimistic update
    setEvents(prev => prev.map(e => e.id === ev.id ? { ...e, isCompleted: newStatus } : e));

    const res = await api.put(`/api/events/${ev.id}`, { isCompleted: newStatus });
    if (!res.isSuccess) {
      // Revert if failed
      loadEvents();
      setIsError(true);
      setMessage(PersianMessages.error(res.error));
    }
  };

  const handleDelete = async (id: string, title: string) => {
    if (!window.confirm(`آیا از حذف رویداد "${title}" اطمینان دارید؟`)) return;

    setLoading(true);
    const res = await api.delete(`/api/events/${id}`);
    setLoading(false);

    if (res.isSuccess) {
      setMessage('رویداد حذف گردید.');
      setIsError(false);
      loadEvents();
    } else {
      setIsError(true);
      setMessage(PersianMessages.error(res.error));
    }
  };

  // Date Formatting Helpers
  const formatPersianDate = (isoString: string) => {
    try {
      const d = new Date(isoString);
      return new Intl.DateTimeFormat('fa-IR', {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
      }).format(d);
    } catch {
      return isoString;
    }
  };

  const formatPersianTime = (isoString: string) => {
    try {
      const d = new Date(isoString);
      return new Intl.DateTimeFormat('fa-IR', {
        hour: '2-digit',
        minute: '2-digit',
      }).format(d);
    } catch {
      return '';
    }
  };

  const formatPersianWeekday = (date: Date) => {
    return new Intl.DateTimeFormat('fa-IR', { weekday: 'long' }).format(date);
  };

  // Filtered Events
  const filteredEvents = events.filter(e => {
    if (filterStatus === 'pending' && e.isCompleted) return false;
    if (filterStatus === 'completed' && !e.isCompleted) return false;
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      const matchTitle = e.title.toLowerCase().includes(q);
      const matchDesc = e.description ? e.description.toLowerCase().includes(q) : false;
      if (!matchTitle && !matchDesc) return false;
    }
    return true;
  });

  // Calendar Month Generation
  const currentYear = selectedDate.getFullYear();
  const currentMonth = selectedDate.getMonth();
  const daysInMonth = new Date(currentYear, currentMonth + 1, 0).getDate();
  const firstDayIndex = new Date(currentYear, currentMonth, 1).getDay(); // 0 is Sunday

  const nextMonth = () => {
    setSelectedDate(new Date(currentYear, currentMonth + 1, 1));
  };

  const prevMonth = () => {
    setSelectedDate(new Date(currentYear, currentMonth - 1, 1));
  };

  const currentMonthLabel = new Intl.DateTimeFormat('fa-IR', {
    month: 'long',
    year: 'numeric',
  }).format(selectedDate);

  return (
    <div className="space-y-6">
      {/* Header Banner */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 bg-white p-5 rounded-2xl border border-slate-200 shadow-sm">
        <div>
          <div className="flex items-center gap-2.5 text-blue-600 mb-1">
            <div className="p-2 rounded-xl bg-blue-50 border border-blue-100">
              <CalendarIcon className="w-5 h-5" />
            </div>
            <h1 className="text-xl font-bold text-slate-900">تقویم و مدیریت رویدادها (Events & Schedule)</h1>
          </div>
          <p className="text-xs text-slate-500 mr-10">
            برنامه‌ریزی جلسات، یادآوری کارهای مهم و زمان‌بندی رویدادهای شخصی با هشدارهای آنی
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={() => loadEvents()}
            disabled={loading}
            className="p-2.5 rounded-xl border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
            title="بروزرسانی"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin text-blue-600' : ''}`} />
          </button>

          <button
            onClick={() => openCreateModal()}
            className="flex items-center gap-2 px-4 py-2.5 bg-blue-600 hover:bg-blue-500 text-white rounded-xl text-xs font-semibold shadow-sm transition-all"
          >
            <Plus className="w-4 h-4" />
            <span>ثبت رویداد جدید</span>
          </button>
        </div>
      </div>

      {/* Notifications Message */}
      {message && (
        <div
          className={`p-4 rounded-xl flex items-center justify-between gap-3 text-sm font-medium border animate-fadeIn ${
            isError
              ? 'bg-rose-50 border-rose-200 text-rose-800'
              : 'bg-emerald-50 border-emerald-200 text-emerald-800'
          }`}
        >
          <div className="flex items-center gap-2.5">
            {isError ? <AlertCircle className="w-4 h-4" /> : <CheckCircle2 className="w-4 h-4" />}
            <span>{message}</span>
          </div>
          <button onClick={() => setMessage(null)} className="text-xs opacity-70 hover:opacity-100">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Control Bar (Views, Status Filter, Search) */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3 bg-white p-3.5 rounded-xl border border-slate-200">
        {/* View Toggle */}
        <div className="flex items-center bg-slate-100 p-1 rounded-lg border border-slate-200/80">
          <button
            onClick={() => setViewMode('calendar')}
            className={`flex-1 py-1.5 rounded-md text-xs font-medium flex items-center justify-center gap-1.5 transition-all ${
              viewMode === 'calendar'
                ? 'bg-white text-blue-600 shadow-sm font-semibold'
                : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            <CalendarDays className="w-3.5 h-3.5" />
            <span>نمای تقویم ماهانه</span>
          </button>
          <button
            onClick={() => setViewMode('agenda')}
            className={`flex-1 py-1.5 rounded-md text-xs font-medium flex items-center justify-center gap-1.5 transition-all ${
              viewMode === 'agenda'
                ? 'bg-white text-blue-600 shadow-sm font-semibold'
                : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            <ListFilter className="w-3.5 h-3.5" />
            <span>نمای لیست و برنامه</span>
          </button>
        </div>

        {/* Status Filter */}
        <div className="flex items-center bg-slate-100 p-1 rounded-lg border border-slate-200/80">
          <button
            onClick={() => setFilterStatus('all')}
            className={`flex-1 py-1.5 rounded-md text-xs font-medium transition-all ${
              filterStatus === 'all'
                ? 'bg-white text-slate-900 shadow-sm font-semibold'
                : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            همه ({events.length})
          </button>
          <button
            onClick={() => setFilterStatus('pending')}
            className={`flex-1 py-1.5 rounded-md text-xs font-medium transition-all ${
              filterStatus === 'pending'
                ? 'bg-white text-amber-600 shadow-sm font-semibold'
                : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            در پیش‌رو ({events.filter(e => !e.isCompleted).length})
          </button>
          <button
            onClick={() => setFilterStatus('completed')}
            className={`flex-1 py-1.5 rounded-md text-xs font-medium transition-all ${
              filterStatus === 'completed'
                ? 'bg-white text-emerald-600 shadow-sm font-semibold'
                : 'text-slate-600 hover:text-slate-900'
            }`}
          >
            انجام‌شده ({events.filter(e => e.isCompleted).length})
          </button>
        </div>

        {/* Search Input */}
        <div className="relative">
          <Search className="w-4 h-4 text-slate-400 absolute right-3 top-2.5" />
          <input
            type="text"
            placeholder="جستجوی رویداد یا توضیحات..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full pl-3 pr-9 py-1.5 bg-slate-50 border border-slate-200 rounded-lg text-xs focus:bg-white focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
          />
        </div>
      </div>

      {/* Main Content Area */}
      {viewMode === 'calendar' ? (
        /* Calendar View */
        <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden">
          {/* Calendar Header Navigation */}
          <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100 bg-slate-50/50">
            <div className="flex items-center gap-3">
              <span className="text-base font-bold text-slate-900">{currentMonthLabel}</span>
              <button
                onClick={() => setSelectedDate(new Date())}
                className="px-2.5 py-1 text-[11px] font-semibold bg-white text-blue-600 border border-blue-200 rounded-lg hover:bg-blue-50 transition-colors shadow-xs"
              >
                امروز
              </button>
            </div>
            <div className="flex items-center gap-1">
              <button
                onClick={prevMonth}
                className="p-1.5 rounded-lg border border-slate-200 hover:bg-white text-slate-600 transition-colors"
                title="ماه قبل"
              >
                <ChevronRight className="w-4 h-4" />
              </button>
              <button
                onClick={nextMonth}
                className="p-1.5 rounded-lg border border-slate-200 hover:bg-white text-slate-600 transition-colors"
                title="ماه بعد"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
            </div>
          </div>

          {/* Calendar Weekday Names */}
          <div className="grid grid-cols-7 border-b border-slate-100 bg-slate-50 text-center text-xs font-semibold text-slate-600 py-2.5">
            {['شنبه', 'یکشنبه', 'دوشنبه', 'سه‌شنبه', 'چهارشنبه', 'پنجشنبه', 'جمعه'].map((day, idx) => (
              <div key={idx} className={idx === 6 ? 'text-rose-500' : ''}>
                {day}
              </div>
            ))}
          </div>

          {/* Calendar Grid Cells */}
          <div className="grid grid-cols-7 auto-rows-fr bg-slate-200 gap-px">
            {/* Blank leading days */}
            {Array.from({ length: (firstDayIndex + 1) % 7 }).map((_, i) => (
              <div key={`blank-${i}`} className="bg-slate-50/60 min-h-[100px] p-2" />
            ))}

            {/* Days in current month */}
            {Array.from({ length: daysInMonth }).map((_, i) => {
              const dayNumber = i + 1;
              const dateObj = new Date(currentYear, currentMonth, dayNumber);
              const dateIsoPrefix = dateObj.toISOString().split('T')[0];

              const dayEvents = filteredEvents.filter(e => e.startAtUtc.startsWith(dateIsoPrefix));
              const isToday = new Date().toISOString().split('T')[0] === dateIsoPrefix;

              return (
                <div
                  key={`day-${dayNumber}`}
                  onClick={() => openCreateModal(dateObj)}
                  className={`bg-white min-h-[110px] p-2 flex flex-col justify-between transition-colors hover:bg-blue-50/40 cursor-pointer group relative ${
                    isToday ? 'bg-blue-50/20' : ''
                  }`}
                >
                  <div className="flex items-center justify-between mb-1.5">
                    <span
                      className={`text-xs font-semibold rounded-full w-6 h-6 flex items-center justify-center ${
                        isToday
                          ? 'bg-blue-600 text-white shadow-xs'
                          : 'text-slate-700 group-hover:text-blue-600'
                      }`}
                    >
                      {dayNumber}
                    </span>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        openCreateModal(dateObj);
                      }}
                      className="opacity-0 group-hover:opacity-100 p-1 text-slate-400 hover:text-blue-600 rounded transition-opacity"
                      title="افزودن رویداد در این روز"
                    >
                      <Plus className="w-3.5 h-3.5" />
                    </button>
                  </div>

                  {/* Day Events Pills */}
                  <div className="space-y-1 overflow-y-auto max-h-[80px]">
                    {dayEvents.map(ev => (
                      <div
                        key={ev.id}
                        onClick={(e) => {
                          e.stopPropagation();
                          openEditModal(ev);
                        }}
                        className={`text-[11px] px-2 py-1 rounded-md border truncate font-medium flex items-center justify-between gap-1 transition-all ${
                          ev.isCompleted
                            ? 'bg-emerald-50 border-emerald-200 text-emerald-800 line-through opacity-80'
                            : 'bg-blue-50 border-blue-200 text-blue-900 hover:border-blue-300'
                        }`}
                        title={ev.title}
                      >
                        <span className="truncate">{ev.title}</span>
                        {ev.reminderMinutesBefore && (
                          <Bell className="w-3 h-3 text-amber-500 shrink-0" />
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      ) : (
        /* Agenda List View */
        <div className="space-y-3">
          {filteredEvents.length === 0 ? (
            <div className="bg-white rounded-2xl border border-slate-200 p-12 text-center">
              <div className="w-12 h-12 rounded-2xl bg-blue-50 border border-blue-100 text-blue-600 flex items-center justify-center mx-auto mb-3">
                <CalendarIcon className="w-6 h-6" />
              </div>
              <h3 className="text-base font-bold text-slate-800 mb-1">هیچ رویدادی یافت نشد</h3>
              <p className="text-xs text-slate-500 max-w-sm mx-auto mb-4">
                شما می‌توانید اولین رویداد یا یادآوری خود را با کلیک روی دکمه زیر ایجاد کنید.
              </p>
              <button
                onClick={() => openCreateModal()}
                className="px-4 py-2 bg-blue-600 hover:bg-blue-500 text-white rounded-xl text-xs font-semibold shadow-sm transition-all"
              >
                ثبت اولین رویداد
              </button>
            </div>
          ) : (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-3.5">
              {filteredEvents.map(ev => {
                const startTime = formatPersianTime(ev.startAtUtc);
                const startDate = formatPersianDate(ev.startAtUtc);
                const isPast = new Date(ev.startAtUtc).getTime() < Date.now();

                return (
                  <div
                    key={ev.id}
                    className={`bg-white rounded-2xl border p-4 shadow-sm transition-all hover:border-blue-300 hover:shadow-md flex flex-col justify-between ${
                      ev.isCompleted
                        ? 'border-slate-200/80 bg-slate-50/40 opacity-85'
                        : isPast
                        ? 'border-slate-200 bg-white'
                        : 'border-blue-100 bg-gradient-to-br from-white to-blue-50/20'
                    }`}
                  >
                    <div>
                      <div className="flex items-start justify-between gap-3 mb-2">
                        <div className="flex items-start gap-2.5">
                          <button
                            onClick={() => toggleEventCompleted(ev)}
                            className="mt-0.5 text-slate-400 hover:text-emerald-600 transition-colors"
                            title={ev.isCompleted ? 'علامت به عنوان انجام نشده' : 'علامت به عنوان انجام شده'}
                          >
                            {ev.isCompleted ? (
                              <CheckCircle2 className="w-5 h-5 text-emerald-600" />
                            ) : (
                              <Circle className="w-5 h-5" />
                            )}
                          </button>
                          <div>
                            <h3
                              className={`text-sm font-bold text-slate-900 leading-snug ${
                                ev.isCompleted ? 'line-through text-slate-500' : ''
                              }`}
                            >
                              {ev.title}
                            </h3>
                            {ev.description && (
                              <p className="text-xs text-slate-500 mt-1 leading-relaxed line-clamp-2">
                                {ev.description}
                              </p>
                            )}
                          </div>
                        </div>

                        <div className="flex items-center gap-1">
                          <button
                            onClick={() => openEditModal(ev)}
                            className="p-1.5 rounded-lg text-slate-400 hover:text-blue-600 hover:bg-blue-50 transition-colors"
                            title="ویرایش"
                          >
                            <Edit3 className="w-4 h-4" />
                          </button>
                          <button
                            onClick={() => handleDelete(ev.id, ev.title)}
                            className="p-1.5 rounded-lg text-slate-400 hover:text-rose-600 hover:bg-rose-50 transition-colors"
                            title="حذف"
                          >
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    </div>

                    <div className="pt-3 mt-3 border-t border-slate-100 flex flex-wrap items-center justify-between gap-2 text-xs">
                      <div className="flex items-center gap-3 text-slate-600 font-medium">
                        <span className="flex items-center gap-1">
                          <CalendarIcon className="w-3.5 h-3.5 text-blue-500" />
                          <span>{startDate}</span>
                        </span>
                        <span className="flex items-center gap-1">
                          <Clock className="w-3.5 h-3.5 text-indigo-500" />
                          <span>{startTime}</span>
                        </span>
                      </div>

                      <div className="flex items-center gap-2">
                        {ev.reminderMinutesBefore ? (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-amber-50 border border-amber-200 text-amber-700 text-[11px] font-medium">
                            <Bell className="w-3 h-3" />
                            <span>{ev.reminderMinutesBefore} دقیقه قبل</span>
                          </span>
                        ) : null}

                        {ev.isCompleted ? (
                          <span className="px-2 py-0.5 rounded-full bg-emerald-50 border border-emerald-200 text-emerald-700 text-[11px] font-semibold">
                            انجام شد
                          </span>
                        ) : isPast ? (
                          <span className="px-2 py-0.5 rounded-full bg-slate-100 border border-slate-200 text-slate-600 text-[11px]">
                            سپری شده
                          </span>
                        ) : (
                          <span className="px-2 py-0.5 rounded-full bg-blue-50 border border-blue-200 text-blue-700 text-[11px] font-semibold">
                            در پیش‌رو
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}

      {/* Create / Edit Modal Dialog */}
      {isModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-fadeIn">
          <div className="bg-white rounded-2xl shadow-xl border border-slate-200 w-full max-w-lg overflow-hidden animate-scaleUp">
            <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between bg-slate-50/50">
              <div className="flex items-center gap-2.5 text-blue-600">
                <CalendarIcon className="w-5 h-5" />
                <h3 className="font-bold text-slate-900 text-base">
                  {editingEvent ? 'ویرایش رویداد' : 'ثبت رویداد جدید'}
                </h3>
              </div>
              <button
                onClick={() => setIsModalOpen(false)}
                className="p-1 text-slate-400 hover:text-slate-700 rounded-lg"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            <form onSubmit={handleSubmit} className="p-6 space-y-4">
              <div>
                <label className="block text-xs font-bold text-slate-700 mb-1">
                  عنوان رویداد <span className="text-rose-500">*</span>
                </label>
                <input
                  type="text"
                  required
                  placeholder="مثال: جلسه بررسی معماری میکروسرویس‌ها"
                  value={formTitle}
                  onChange={(e) => setFormTitle(e.target.value)}
                  className="w-full px-3.5 py-2 text-sm border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
                />
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-700 mb-1">
                  توضیحات و جزئیات (اختیاری)
                </label>
                <textarea
                  rows={3}
                  placeholder="محل برگزاری، لینک میتینگ، نکات مهم یا دستور جلسه..."
                  value={formDescription}
                  onChange={(e) => setFormDescription(e.target.value)}
                  className="w-full px-3.5 py-2 text-sm border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 transition-all"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-bold text-slate-700 mb-1">
                    تاریخ شروع <span className="text-rose-500">*</span>
                  </label>
                  <input
                    type="date"
                    required
                    value={formStartDate}
                    onChange={(e) => setFormStartDate(e.target.value)}
                    className="w-full px-3 py-2 text-xs border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-slate-700 mb-1">
                    ساعت شروع
                  </label>
                  <input
                    type="time"
                    value={formStartTime}
                    onChange={(e) => setFormStartTime(e.target.value)}
                    className="w-full px-3 py-2 text-xs border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                  />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-bold text-slate-700 mb-1">
                    تاریخ پایان (اختیاری)
                  </label>
                  <input
                    type="date"
                    value={formEndDate}
                    onChange={(e) => setFormEndDate(e.target.value)}
                    className="w-full px-3 py-2 text-xs border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-slate-700 mb-1">
                    ساعت پایان
                  </label>
                  <input
                    type="time"
                    value={formEndTime}
                    onChange={(e) => setFormEndTime(e.target.value)}
                    className="w-full px-3 py-2 text-xs border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500"
                  />
                </div>
              </div>

              <div>
                <label className="block text-xs font-bold text-slate-700 mb-1">
                  یادآوری و هشدار اعلان
                </label>
                <select
                  value={formReminder === null ? '' : formReminder}
                  onChange={(e) => setFormReminder(e.target.value === '' ? null : Number(e.target.value))}
                  className="w-full px-3.5 py-2 text-xs border border-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-500 bg-white"
                >
                  <option value="">بدون یادآوری</option>
                  <option value="5">۵ دقیقه قبل</option>
                  <option value="15">۱۵ دقیقه قبل</option>
                  <option value="30">۳۰ دقیقه قبل</option>
                  <option value="60">۱ ساعت قبل</option>
                  <option value="1440">۱ روز قبل</option>
                </select>
                <p className="text-[11px] text-slate-400 mt-1">
                  سیستم در زمان مقرر اعلان صوتی و زنگ یادآوری را از طریق SignalR به حساب شما ارسال می‌کند.
                </p>
              </div>

              <div className="pt-4 border-t border-slate-100 flex items-center justify-end gap-2.5">
                <button
                  type="button"
                  onClick={() => setIsModalOpen(false)}
                  className="px-4 py-2 text-xs font-semibold text-slate-600 hover:bg-slate-100 rounded-xl transition-colors"
                >
                  انصراف
                </button>
                <button
                  type="submit"
                  disabled={loading}
                  className="px-5 py-2 text-xs font-semibold bg-blue-600 hover:bg-blue-500 text-white rounded-xl shadow-sm transition-all flex items-center gap-1.5"
                >
                  {loading && <RefreshCw className="w-3.5 h-3.5 animate-spin" />}
                  <span>{editingEvent ? 'ذخیره تغییرات' : 'ثبت رویداد'}</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};
