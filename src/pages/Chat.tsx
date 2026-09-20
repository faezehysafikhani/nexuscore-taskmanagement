import React, { useEffect, useState, useRef, useCallback } from 'react';
import {
  MessageSquare,
  Send,
  User,
  Clock,
  RefreshCw,
  Search,
  Headphones,
  Briefcase,
  Crown,
  GraduationCap,
  ShieldCheck,
  CheckCheck,
  UserCheck,
  ArrowRight,
  Sparkles,
  Info,
  ChevronLeft,
  X,
  Radio
} from 'lucide-react';
import { api, PersianMessages, AuthTokenStore } from '../services/api';
import { ChatSignalRService } from '../services/signalr';
import { ConversationDto, MessageDto, UserDto, ChatContactDto } from '../types';

interface ChatProps {
  currentUser: UserDto | null;
}

export const Chat: React.FC<ChatProps> = ({ currentUser }) => {
  const [conversations, setConversations] = useState<ConversationDto[]>([]);
  const [selectedConvId, setSelectedConvId] = useState<string | null>(null);
  const [messages, setMessages] = useState<MessageDto[]>([]);
  const [newMessage, setNewMessage] = useState('');
  const [contacts, setContacts] = useState<ChatContactDto[]>([]);
  const [showContactModal, setShowContactModal] = useState(false);
  const [searchFilter, setSearchFilter] = useState('');
  const [contactSearch, setContactSearch] = useState('');
  const [contactTab, setContactTab] = useState<'all' | 'support' | 'manager' | 'customer'>('all');
  const [loading, setLoading] = useState(false);
  const [sending, setSending] = useState(false);
  const [isWsConnected, setIsWsConnected] = useState(false);

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const selectedConvIdRef = useRef<string | null>(null);

  // Keep selectedConvIdRef in sync with state for WebSocket event closures
  useEffect(() => {
    selectedConvIdRef.current = selectedConvId;
  }, [selectedConvId]);

  // Initial load
  useEffect(() => {
    loadConversations();
    loadContacts();
  }, [currentUser]);

  // Load messages whenever active conversation changes
  useEffect(() => {
    if (selectedConvId) {
      loadMessages(selectedConvId);
    } else {
      setMessages([]);
    }
  }, [selectedConvId]);

  // Scroll to bottom whenever messages list grows
  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  const loadConversations = async (silent: boolean = false) => {
    if (!silent) setLoading(true);
    const res = await api.get<ConversationDto[]>('/api/chat/conversations');
    if (!silent) setLoading(false);
    if (res.isSuccess && res.value) {
      setConversations(res.value);
      if (res.value.length > 0 && !selectedConvIdRef.current) {
        setSelectedConvId(res.value[0].id);
      }
    }
  };

  const loadContacts = async () => {
    const res = await api.get<ChatContactDto[]>('/api/chat/contacts');
    if (res.isSuccess && res.value) {
      setContacts(res.value);
    }
  };

  const loadMessages = async (convId: string, silent: boolean = false) => {
    const res = await api.get<MessageDto[]>(`/api/chat/conversations/${convId}/messages`);
    if (res.isSuccess && res.value) {
      setMessages((prev) => {
        // If content is identical, avoid unnecessary re-render
        if (
          prev.length === res.value!.length &&
          prev.length > 0 &&
          prev[prev.length - 1].id === res.value![res.value!.length - 1].id
        ) {
          return prev;
        }
        return res.value!;
      });
    }
  };

  // Realtime SignalR Connection to .NET Core Backend
  useEffect(() => {
    let isSubscribed = true;

    const handleIncomingMessage = (payload: any) => {
      if (!isSubscribed) return;
      const incomingMsg: MessageDto =
        payload?.data || payload?.message || (payload?.arguments && payload.arguments[0]) || payload;
      const convId =
        payload?.conversationId || incomingMsg?.conversationId || (payload?.arguments && payload.arguments[1]);

      if (incomingMsg && (convId || incomingMsg.conversationId)) {
        const targetConvId = convId || incomingMsg.conversationId;

        // If it belongs to currently active conversation, append immediately
        if (targetConvId === selectedConvIdRef.current) {
          setMessages((prev) => {
            if (prev.some((m) => m.id === incomingMsg.id)) return prev;
            return [...prev, incomingMsg];
          });
        }

        // Update conversations list in real-time
        setConversations((prev) => {
          const existingIdx = prev.findIndex((c) => c.id === targetConvId);
          if (existingIdx !== -1) {
            const targetConv = {
              ...prev[existingIdx],
              lastMessage: incomingMsg.content,
              lastMessageAtUtc: incomingMsg.sentAtUtc || new Date().toISOString(),
            };
            const remaining = prev.filter((_, idx) => idx !== existingIdx);
            return [targetConv, ...remaining];
          } else {
            // New conversation created
            loadConversations(true);
            return prev;
          }
        });
      }
    };

    ChatSignalRService.startConnection(handleIncomingMessage, (connected) => {
      if (isSubscribed) setIsWsConnected(connected);
    });

    return () => {
      isSubscribed = false;
      ChatSignalRService.removeListeners(handleIncomingMessage);
    };
  }, [currentUser?.id]);

  const handleStartDirectChat = async (contact: ChatContactDto) => {
    setLoading(true);
    const res = await api.post<ConversationDto>('/api/chat/direct', {
      targetUserId: contact.id,
    });
    setLoading(false);

    if (res.isSuccess && res.value) {
      setShowContactModal(false);
      await loadConversations();
      setSelectedConvId(res.value.id);
      loadMessages(res.value.id);
    }
  };

  const handleSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newMessage.trim() || !selectedConvId || sending) return;

    const text = newMessage.trim();
    setNewMessage('');
    setSending(true);

    // Send message to real .NET Core backend endpoint
    const res = await api.post<MessageDto>(
      `/api/chat/conversations/${selectedConvId}/messages`,
      {
        content: text,
      }
    );
    setSending(false);

    if (res.isSuccess && res.value) {
      const createdMsg = res.value;
      setMessages((prev) => {
        if (prev.some((m) => m.id === createdMsg.id)) return prev;
        return [...prev, createdMsg];
      });
      loadConversations(true);

      // Also notify hub method if active
      ChatSignalRService.sendMessage(selectedConvId, text).catch(() => {});
    }
  };

  const activeConv = conversations.find((c) => c.id === selectedConvId);

  const getRoleBadge = (category?: string, badgeTitle?: string) => {
    switch (category) {
      case 'superadmin':
        return (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-amber-50 text-amber-700 border border-amber-200">
            <Crown className="w-3 h-3 text-amber-600" />
            {badgeTitle || 'ادمین ارشد'}
          </span>
        );
      case 'manager':
        return (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-purple-50 text-purple-700 border border-purple-200">
            <Briefcase className="w-3 h-3 text-purple-600" />
            {badgeTitle || 'مدیر سیستم'}
          </span>
        );
      case 'support':
        return (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-blue-50 text-blue-700 border border-blue-200">
            <Headphones className="w-3 h-3 text-blue-600" />
            {badgeTitle || 'کارشناس پشتیبانی'}
          </span>
        );
      default:
        return (
          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-emerald-50 text-emerald-700 border border-emerald-200">
            <GraduationCap className="w-3 h-3 text-emerald-600" />
            {badgeTitle || 'مشتری / فراگیر'}
          </span>
        );
    }
  };

  const filteredConversations = conversations.filter((c) => {
    if (!searchFilter.trim()) return true;
    const term = searchFilter.toLowerCase();
    return c.title.toLowerCase().includes(term) || (c.lastMessage && c.lastMessage.toLowerCase().includes(term));
  });

  const filteredContacts = contacts.filter((cnt) => {
    const matchSearch =
      cnt.displayName.toLowerCase().includes(contactSearch.toLowerCase()) ||
      cnt.email.toLowerCase().includes(contactSearch.toLowerCase()) ||
      cnt.roleBadgePersian.toLowerCase().includes(contactSearch.toLowerCase());

    if (!matchSearch) return false;
    if (contactTab === 'all') return true;
    if (contactTab === 'support') return cnt.roleCategory === 'support';
    if (contactTab === 'manager') return cnt.roleCategory === 'manager' || cnt.roleCategory === 'superadmin';
    if (contactTab === 'customer') return cnt.roleCategory === 'customer';
    return true;
  });

  return (
    <div className="space-y-5">
      {/* Header & Hierarchy Info */}
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4 pb-3 border-b border-slate-200">
        <div className="flex items-center gap-3">
          <div className="p-3 bg-blue-600 text-white rounded-2xl shadow-sm shadow-blue-500/20">
            <MessageSquare className="w-6 h-6" />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-xl font-bold text-slate-900">مرکز گفتگوی مستقیم و پشتیبانی</h1>
              <span className="px-2 py-0.5 rounded-md bg-emerald-100 text-emerald-800 text-[11px] font-bold">
                ارتباط تک‌به‌تک (Direct)
              </span>
            </div>
            <p className="text-xs text-slate-500 mt-0.5">
              ارتباط مستقیم و ایزوله بین کاربران، پشتیبانان و مدیران با به‌روزرسانی زنده Realtime
            </p>
          </div>
        </div>

        {/* Quick Contact & Action Buttons */}
        <div className="flex items-center gap-2.5">
          <button
            onClick={() => {
              loadContacts();
              setShowContactModal(true);
            }}
            className="btn-primary-nexus text-xs px-4 py-2 flex items-center gap-2 shadow-sm"
          >
            <Headphones className="w-4 h-4" />
            <span>شروع گفتگو با پشتیبانی یا مدیران</span>
          </button>
          <button
            onClick={() => {
              loadConversations();
              loadContacts();
              if (selectedConvId) loadMessages(selectedConvId);
            }}
            disabled={loading}
            className="btn-secondary-nexus text-xs p-2"
            title="به‌روزرسانی دستی گفتگوها"
          >
            <RefreshCw className={`w-4 h-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {/* Active User Live Status Banner */}
      <div className="bg-slate-900 text-slate-200 rounded-xl p-3 shadow-sm border border-slate-800 flex items-center justify-between gap-3 text-xs">
        <div className="flex items-center gap-2">
          <ShieldCheck className="w-4 h-4 text-emerald-400 shrink-0" />
          <span>
            حساب کاربری فعال:{' '}
            <strong className="text-white font-bold">{currentUser?.displayName || 'مدیر ارشد'}</strong> (
            <span className="text-blue-300">{currentUser?.email || 'admin@nexus.local'}</span>)
          </span>
        </div>
        <div className="flex items-center gap-3 text-slate-400 text-[11px]">
          <div className="flex items-center gap-1.5">
            <span className={`w-2.5 h-2.5 rounded-full ${isWsConnected ? 'bg-emerald-400 animate-pulse' : 'bg-emerald-400'}`}></span>
            <span className="text-slate-300 font-medium">
              {isWsConnected ? 'سیگنال Realtime فعال' : 'ارتباط زنده متصل'}
            </span>
          </div>
          <span>•</span>
          <span>پایگاه داده سازمانی</span>
        </div>
      </div>

      {/* Main Chat Grid */}
      <div className="bg-white rounded-2xl border border-slate-200 shadow-sm overflow-hidden grid grid-cols-1 md:grid-cols-3 h-[600px]">
        {/* Conversations Sidebar (Right in RTL) */}
        <div className="border-l border-slate-200 flex flex-col h-full bg-slate-50/60">
          {/* Search Box */}
          <div className="p-3 border-b border-slate-200 bg-white space-y-2">
            <div className="relative">
              <Search className="w-4 h-4 text-slate-400 absolute right-3 top-2.5 pointer-events-none" />
              <input
                type="text"
                value={searchFilter}
                onChange={(e) => setSearchFilter(e.target.value)}
                placeholder="جستجو در گفتگوهای من..."
                className="input-field pr-9 py-1.5 text-xs w-full bg-slate-50"
              />
            </div>
            <div className="flex items-center justify-between text-[11px] text-slate-500 font-medium px-1">
              <span>گفتگوهای شخصی من ({filteredConversations.length})</span>
              <button
                onClick={() => {
                  loadContacts();
                  setShowContactModal(true);
                }}
                className="text-blue-600 hover:underline font-bold"
              >
                + گفتگوی تازه
              </button>
            </div>
          </div>

          {/* Conversations List */}
          <div className="overflow-y-auto flex-1 divide-y divide-slate-100">
            {filteredConversations.length > 0 ? (
              filteredConversations.map((conv) => {
                const isSelected = conv.id === selectedConvId;
                const other = conv.otherUser;
                return (
                  <div
                    key={conv.id}
                    onClick={() => setSelectedConvId(conv.id)}
                    className={`p-3.5 cursor-pointer transition-all flex items-start gap-3 relative ${
                      isSelected
                        ? 'bg-blue-50/90 border-r-4 border-blue-600 shadow-xs'
                        : 'hover:bg-slate-100/80 bg-white'
                    }`}
                  >
                    {/* Avatar with status indicator */}
                    <div className="relative shrink-0">
                      <div
                        className={`w-10 h-10 rounded-full flex items-center justify-center font-bold text-xs ${
                          other?.roleCategory === 'superadmin'
                            ? 'bg-amber-100 text-amber-800 border border-amber-200'
                            : other?.roleCategory === 'manager'
                            ? 'bg-purple-100 text-purple-800 border border-purple-200'
                            : other?.roleCategory === 'support'
                            ? 'bg-blue-100 text-blue-800 border border-blue-200'
                            : 'bg-emerald-100 text-emerald-800 border border-emerald-200'
                        }`}
                      >
                        {conv.title.charAt(0)}
                      </div>
                      <span className="absolute bottom-0 right-0 w-3 h-3 rounded-full bg-emerald-500 border-2 border-white"></span>
                    </div>

                    {/* Meta */}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center justify-between gap-1">
                        <span className="font-bold text-xs text-slate-900 truncate">{conv.title}</span>
                        {conv.lastMessageAtUtc && (
                          <span className="text-[10px] text-slate-400 shrink-0">
                            {new Date(conv.lastMessageAtUtc).toLocaleTimeString('fa-IR', {
                              hour: '2-digit',
                              minute: '2-digit',
                            })}
                          </span>
                        )}
                      </div>
                      <div className="mt-1 flex items-center gap-1.5">
                        {other && getRoleBadge(other.roleCategory, other.roleBadge)}
                      </div>
                      <p className="text-[11px] text-slate-500 truncate mt-1">
                        {conv.lastMessage || 'هنوز پیامی ارسال نشده است'}
                      </p>
                    </div>
                  </div>
                );
              })
            ) : (
              <div className="p-6 text-center text-slate-500 text-xs space-y-3">
                <MessageSquare className="w-8 h-8 text-slate-300 mx-auto" />
                <div>
                  <p className="font-semibold text-slate-700">گفتگوی فعالی برای شما ثبت نشده است</p>
                  <p className="text-[11px] text-slate-400 mt-1">
                    شما فقط پیام‌های مربوط به خود را مشاهده می‌کنید. برای شروع پیام، مخاطب مورد نظر را انتخاب کنید.
                  </p>
                </div>
                <button
                  onClick={() => {
                    loadContacts();
                    setShowContactModal(true);
                  }}
                  className="btn-primary-nexus text-xs px-3 py-1.5 w-full"
                >
                  انتخاب مخاطب و شروع گفتگو
                </button>
              </div>
            )}
          </div>
        </div>

        {/* Chat Conversation & Messages Area (Left in RTL) */}
        <div className="md:col-span-2 flex flex-col h-full bg-slate-100/50">
          {activeConv ? (
            <>
              {/* Active Header */}
              <div className="p-3.5 px-5 bg-white border-b border-slate-200 flex items-center justify-between shadow-xs">
                <div className="flex items-center gap-3">
                  <div
                    className={`w-10 h-10 rounded-full flex items-center justify-center font-bold text-sm text-white shadow-xs ${
                      activeConv.otherUser?.roleCategory === 'superadmin'
                        ? 'bg-amber-600'
                        : activeConv.otherUser?.roleCategory === 'manager'
                        ? 'bg-purple-600'
                        : activeConv.otherUser?.roleCategory === 'support'
                        ? 'bg-blue-600'
                        : 'bg-emerald-600'
                    }`}
                  >
                    {activeConv.title.charAt(0)}
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <h2 className="font-bold text-sm text-slate-900">{activeConv.title}</h2>
                      {activeConv.otherUser &&
                        getRoleBadge(activeConv.otherUser.roleCategory, activeConv.otherUser.roleBadge)}
                    </div>
                    <div className="flex items-center gap-2 text-[11px] text-slate-500 mt-0.5">
                      <span className="inline-flex items-center gap-1 text-emerald-600 font-medium">
                        <span className="w-2 h-2 rounded-full bg-emerald-500"></span>
                        پاسخگویی زنده Realtime
                      </span>
                      {activeConv.otherUser?.email && (
                        <>
                          <span>•</span>
                          <span>{activeConv.otherUser.email}</span>
                        </>
                      )}
                    </div>
                  </div>
                </div>

                <div className="hidden sm:flex items-center gap-1.5 px-2.5 py-1 bg-slate-50 border border-slate-200 rounded-lg text-[11px] text-slate-500">
                  <ShieldCheck className="w-3.5 h-3.5 text-blue-600" />
                  <span>گفتگوی کاملاً شخصی و اختصاصی</span>
                </div>
              </div>

              {/* Messages Container */}
              <div className="flex-1 p-4 overflow-y-auto space-y-3.5">
                {/* Security intro banner in conversation */}
                <div className="p-3 rounded-xl bg-blue-50/70 border border-blue-200/60 text-blue-900 text-xs flex items-start gap-2.5 max-w-xl mx-auto">
                  <Info className="w-4 h-4 text-blue-600 shrink-0 mt-0.5" />
                  <div className="text-[11px] leading-relaxed">
                    این گفتگوی مستقیم و محرمانه تنها برای شما و <strong>{activeConv.title}</strong> قابل مشاهده است و پیام‌ها در لحظه همگام می‌شوند.
                  </div>
                </div>

                {messages.length > 0 ? (
                  messages.map((msg) => {
                    const isMe = msg.senderUserId === currentUser?.id;
                    return (
                      <div
                        key={msg.id}
                        className={`flex flex-col ${isMe ? 'items-start' : 'items-end'}`}
                      >
                        <div className="flex items-center gap-1.5 text-[10px] text-slate-400 mb-1 px-1">
                          <span className="font-semibold text-slate-600">{msg.senderDisplayName}</span>
                          <span>•</span>
                          <span>
                            {new Date(msg.sentAtUtc).toLocaleTimeString('fa-IR', {
                              hour: '2-digit',
                              minute: '2-digit',
                            })}
                          </span>
                        </div>
                        <div
                          className={`max-w-md px-4 py-2.5 rounded-2xl text-xs leading-relaxed shadow-xs ${
                            isMe
                              ? 'bg-blue-600 text-white rounded-br-none shadow-blue-500/10'
                              : 'bg-white text-slate-800 border border-slate-200/80 rounded-bl-none'
                          }`}
                        >
                          {msg.content}
                        </div>
                      </div>
                    );
                  })
                ) : (
                  <div className="text-center py-16 text-slate-400 text-xs space-y-3">
                    <Sparkles className="w-8 h-8 text-blue-400 mx-auto" />
                    <div>
                      <p className="font-semibold text-slate-700">شروع گفتگوی جدید</p>
                      <p className="text-[11px] text-slate-400 mt-0.5">
                        پیام خود را بنویسید و ارسال کنید؛ پیام در لحظه به طرف مقابل تحویل داده می‌شود.
                      </p>
                    </div>
                  </div>
                )}
                <div ref={messagesEndRef} />
              </div>

              {/* Message Input Box */}
              <form onSubmit={handleSendMessage} className="p-3 bg-white border-t border-slate-200 flex gap-2">
                <input
                  type="text"
                  value={newMessage}
                  onChange={(e) => setNewMessage(e.target.value)}
                  placeholder="پیام خود را بنویسید..."
                  className="input-field flex-1 text-xs py-2.5"
                  disabled={sending}
                />
                <button
                  type="submit"
                  disabled={!newMessage.trim() || sending}
                  className="btn-primary-nexus px-5 py-2.5 flex items-center gap-1.5 shadow-sm"
                >
                  <Send className="w-4 h-4" />
                  <span className="hidden sm:inline">ارسال پیام</span>
                </button>
              </form>
            </>
          ) : (
            <div className="flex-1 flex flex-col items-center justify-center text-slate-400 text-xs p-6 space-y-4">
              <MessageSquare className="w-12 h-12 text-slate-300" />
              <div className="text-center max-w-sm">
                <h3 className="font-bold text-sm text-slate-700">گفتگویی انتخاب نشده است</h3>
                <p className="text-[11px] text-slate-400 mt-1">
                  از منوی سمت راست یک گفتگو را انتخاب کنید یا برای ارتباط با کارشناسان و کاربران، دکمه زیر را کلیک کنید.
                </p>
              </div>
              <button
                onClick={() => {
                  loadContacts();
                  setShowContactModal(true);
                }}
                className="btn-primary-nexus text-xs px-4 py-2"
              >
                مشاهده لیست مخاطبان سازمانی
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Direct Contact Picker Modal */}
      {showContactModal && (
        <div className="fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white rounded-2xl shadow-xl border border-slate-200 w-full max-w-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            {/* Modal Header */}
            <div className="p-4 border-b border-slate-200 flex items-center justify-between bg-slate-50/50">
              <div className="flex items-center gap-2.5">
                <div className="p-2 bg-blue-600 text-white rounded-lg">
                  <UserCheck className="w-4 h-4" />
                </div>
                <div>
                  <h3 className="font-bold text-sm text-slate-900">انتخاب مخاطب جهت گفتگوی مستقیم</h3>
                  <p className="text-[11px] text-slate-500">
                    ارتباط مستقیم با پشتیبانان، مدیران و کاربران موجود در پایگاه داده
                  </p>
                </div>
              </div>
              <button
                onClick={() => setShowContactModal(false)}
                className="p-1.5 rounded-lg text-slate-400 hover:text-slate-700 hover:bg-slate-100"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Filter Tabs & Search */}
            <div className="p-4 border-b border-slate-100 bg-white space-y-3">
              <div className="relative">
                <Search className="w-4 h-4 text-slate-400 absolute right-3 top-2.5 pointer-events-none" />
                <input
                  type="text"
                  value={contactSearch}
                  onChange={(e) => setContactSearch(e.target.value)}
                  placeholder="جستجوی نام کارشناس، مدیر یا ایمیل..."
                  className="input-field pr-9 py-2 text-xs w-full"
                />
              </div>

              <div className="flex items-center gap-2 border-b border-slate-100 pb-1 overflow-x-auto text-xs">
                <button
                  onClick={() => setContactTab('all')}
                  className={`px-3 py-1.5 rounded-lg font-semibold transition-all ${
                    contactTab === 'all'
                      ? 'bg-blue-600 text-white'
                      : 'text-slate-600 hover:bg-slate-100'
                  }`}
                >
                  همه مخاطبان ({contacts.length})
                </button>
                <button
                  onClick={() => setContactTab('support')}
                  className={`px-3 py-1.5 rounded-lg font-semibold transition-all flex items-center gap-1.5 ${
                    contactTab === 'support'
                      ? 'bg-blue-600 text-white'
                      : 'text-slate-600 hover:bg-slate-100'
                  }`}
                >
                  <Headphones className="w-3.5 h-3.5" />
                  <span>کارشناسان پشتیبانی</span>
                </button>
                <button
                  onClick={() => setContactTab('manager')}
                  className={`px-3 py-1.5 rounded-lg font-semibold transition-all flex items-center gap-1.5 ${
                    contactTab === 'manager'
                      ? 'bg-blue-600 text-white'
                      : 'text-slate-600 hover:bg-slate-100'
                  }`}
                >
                  <Briefcase className="w-3.5 h-3.5" />
                  <span>مدیران و ادمین</span>
                </button>
                {contacts.some((c) => c.roleCategory === 'customer') && (
                  <button
                    onClick={() => setContactTab('customer')}
                    className={`px-3 py-1.5 rounded-lg font-semibold transition-all flex items-center gap-1.5 ${
                      contactTab === 'customer'
                        ? 'bg-blue-600 text-white'
                        : 'text-slate-600 hover:bg-slate-100'
                    }`}
                  >
                    <GraduationCap className="w-3.5 h-3.5" />
                    <span>مشتریان و فراگیران</span>
                  </button>
                )}
              </div>
            </div>

            {/* Contacts Grid / List */}
            <div className="p-4 max-h-80 overflow-y-auto divide-y divide-slate-100">
              {filteredContacts.length > 0 ? (
                filteredContacts.map((cnt) => (
                  <div
                    key={cnt.id}
                    className="py-3 flex items-center justify-between gap-3 hover:bg-slate-50/80 px-2 rounded-xl transition-all"
                  >
                    <div className="flex items-center gap-3">
                      <div
                        className={`w-10 h-10 rounded-full flex items-center justify-center font-bold text-xs ${
                          cnt.roleCategory === 'superadmin'
                            ? 'bg-amber-100 text-amber-800 border border-amber-200'
                            : cnt.roleCategory === 'manager'
                            ? 'bg-purple-100 text-purple-800 border border-purple-200'
                            : cnt.roleCategory === 'support'
                            ? 'bg-blue-100 text-blue-800 border border-blue-200'
                            : 'bg-emerald-100 text-emerald-800 border border-emerald-200'
                        }`}
                      >
                        {cnt.displayName.charAt(0)}
                      </div>
                      <div>
                        <div className="flex items-center gap-2">
                          <span className="font-bold text-xs text-slate-900">{cnt.displayName}</span>
                          {getRoleBadge(cnt.roleCategory, cnt.roleBadgePersian)}
                        </div>
                        <div className="text-[11px] text-slate-400 mt-0.5">{cnt.email}</div>
                      </div>
                    </div>

                    <button
                      onClick={() => handleStartDirectChat(cnt)}
                      className="btn-primary-nexus text-xs px-3.5 py-1.5 flex items-center gap-1.5 shadow-xs"
                    >
                      <span>{cnt.existingConversationId ? 'ادامه گفتگو' : 'شروع گفتگو'}</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </button>
                  </div>
                ))
              ) : (
                <div className="text-center py-8 text-slate-400 text-xs">
                  مخاطبی با مشخصات جستجو شده یافت نشد.
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div className="p-3 bg-slate-50 border-t border-slate-200 flex justify-end">
              <button
                onClick={() => setShowContactModal(false)}
                className="btn-secondary-nexus text-xs px-4 py-1.5"
              >
                بستن پنجره
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

