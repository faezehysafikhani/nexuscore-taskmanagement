-- =============================================================================
-- NexusCore Enterprise Platform - Supabase PostgreSQL Schema
-- Target Project: https://nyczzsdkzdscyffbpdun.supabase.co
-- =============================================================================

-- Enable UUID extension
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- -----------------------------------------------------------------------------
-- 1. Multi-Tenancy (Tenants)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tenants (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    name VARCHAR(200) NOT NULL,
    slug VARCHAR(100) NOT NULL UNIQUE,
    description TEXT,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    updated_at_utc TIMESTAMPTZ
);

-- -----------------------------------------------------------------------------
-- 2. Identity & Access Management (Users, Roles, Permissions)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.users (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE CASCADE,
    email VARCHAR(256) NOT NULL,
    display_name VARCHAR(150) NOT NULL,
    password_hash VARCHAR(500) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    last_login_at_utc TIMESTAMPTZ,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    updated_at_utc TIMESTAMPTZ,
    CONSTRAINT uq_users_tenant_email UNIQUE(tenant_id, email)
);

CREATE TABLE IF NOT EXISTS public.roles (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID REFERENCES public.tenants(id) ON DELETE CASCADE,
    name VARCHAR(100) NOT NULL,
    description TEXT,
    is_system BOOLEAN NOT NULL DEFAULT false,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now())
);

CREATE TABLE IF NOT EXISTS public.permissions (
    id VARCHAR(100) PRIMARY KEY,
    name VARCHAR(150) NOT NULL UNIQUE,
    module VARCHAR(50) NOT NULL,
    description TEXT
);

CREATE TABLE IF NOT EXISTS public.user_roles (
    user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    role_id UUID NOT NULL REFERENCES public.roles(id) ON DELETE CASCADE,
    assigned_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE IF NOT EXISTS public.role_permissions (
    role_id UUID NOT NULL REFERENCES public.roles(id) ON DELETE CASCADE,
    permission_id VARCHAR(100) NOT NULL REFERENCES public.permissions(id) ON DELETE CASCADE,
    assigned_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    PRIMARY KEY (role_id, permission_id)
);

-- -----------------------------------------------------------------------------
-- 3. Platform Settings & Audit Logs
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.platform_settings (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID REFERENCES public.tenants(id) ON DELETE CASCADE,
    key VARCHAR(100) NOT NULL,
    value TEXT,
    group_name VARCHAR(50) NOT NULL DEFAULT 'General',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    updated_at_utc TIMESTAMPTZ,
    CONSTRAINT uq_platform_settings_tenant_key UNIQUE(tenant_id, key)
);

CREATE TABLE IF NOT EXISTS public.audit_logs (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID REFERENCES public.tenants(id) ON DELETE SET NULL,
    user_id UUID REFERENCES public.users(id) ON DELETE SET NULL,
    action VARCHAR(100) NOT NULL,
    entity_name VARCHAR(100),
    entity_id VARCHAR(100),
    details TEXT,
    ip_address VARCHAR(45) DEFAULT '127.0.0.1',
    occurred_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now())
);

-- -----------------------------------------------------------------------------
-- 4. Tasks Module
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tasks (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE CASCADE,
    title VARCHAR(250) NOT NULL,
    description TEXT,
    status VARCHAR(50) NOT NULL DEFAULT 'Todo', -- Todo, InProgress, Done
    priority VARCHAR(50) NOT NULL DEFAULT 'Medium', -- Low, Medium, High, Critical
    assigned_user_id UUID REFERENCES public.users(id) ON DELETE SET NULL,
    due_date_utc TIMESTAMPTZ,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    updated_at_utc TIMESTAMPTZ
);

-- -----------------------------------------------------------------------------
-- 5. Events & Calendar Module
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.events (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    title VARCHAR(250) NOT NULL,
    description TEXT,
    start_at_utc TIMESTAMPTZ NOT NULL,
    end_at_utc TIMESTAMPTZ,
    is_completed BOOLEAN NOT NULL DEFAULT false,
    reminder_minutes_before INTEGER,
    reminder_sent BOOLEAN NOT NULL DEFAULT false,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    updated_at_utc TIMESTAMPTZ
);

-- -----------------------------------------------------------------------------
-- 6. Chat & Real-Time Messaging Module
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.conversations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE CASCADE,
    title VARCHAR(200) NOT NULL,
    is_group BOOLEAN NOT NULL DEFAULT true,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now())
);

CREATE TABLE IF NOT EXISTS public.conversation_participants (
    conversation_id UUID NOT NULL REFERENCES public.conversations(id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    joined_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    PRIMARY KEY (conversation_id, user_id)
);

CREATE TABLE IF NOT EXISTS public.chat_messages (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    conversation_id UUID NOT NULL REFERENCES public.conversations(id) ON DELETE CASCADE,
    sender_user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    content TEXT NOT NULL,
    sent_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now())
);

-- -----------------------------------------------------------------------------
-- 7. Ticketing & Support Module
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.tickets (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tenant_id UUID NOT NULL REFERENCES public.tenants(id) ON DELETE CASCADE,
    title VARCHAR(250) NOT NULL,
    description TEXT NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'Open', -- Open, InProgress, Resolved, Closed
    priority VARCHAR(50) NOT NULL DEFAULT 'Medium', -- Low, Medium, High, Critical
    created_user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    assigned_user_id UUID REFERENCES public.users(id) ON DELETE SET NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now()),
    updated_at_utc TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS public.ticket_comments (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    ticket_id UUID NOT NULL REFERENCES public.tickets(id) ON DELETE CASCADE,
    author_user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    comment TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now())
);

-- -----------------------------------------------------------------------------
-- 8. Notifications Module
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS public.notifications (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id UUID NOT NULL REFERENCES public.users(id) ON DELETE CASCADE,
    title VARCHAR(200) NOT NULL,
    message TEXT NOT NULL,
    type VARCHAR(50) NOT NULL DEFAULT 'Info', -- Info, Success, Warning, Error
    is_read BOOLEAN NOT NULL DEFAULT false,
    link VARCHAR(255),
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT timezone('utc'::text, now())
);

-- -----------------------------------------------------------------------------
-- 9. Indices for High Performance Multi-Tenant Queries
-- -----------------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS idx_users_tenant ON public.users(tenant_id);
CREATE INDEX IF NOT EXISTS idx_tasks_tenant ON public.tasks(tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_events_tenant_user ON public.events(tenant_id, user_id, start_at_utc);
CREATE INDEX IF NOT EXISTS idx_chat_messages_conv ON public.chat_messages(conversation_id, sent_at_utc);
CREATE INDEX IF NOT EXISTS idx_tickets_tenant ON public.tickets(tenant_id, status);
CREATE INDEX IF NOT EXISTS idx_notifications_user ON public.notifications(user_id, is_read);
CREATE INDEX IF NOT EXISTS idx_audit_logs_tenant ON public.audit_logs(tenant_id, occurred_at_utc DESC);

-- -----------------------------------------------------------------------------
-- 10. Initial Seed Data (Admin user, Default Tenant, Core Permissions)
-- -----------------------------------------------------------------------------
INSERT INTO public.tenants (id, name, slug, description, is_active)
VALUES ('11111111-1111-1111-1111-111111111111', 'Default Organization', 'default', 'سازمان پیش‌فرض سیستم نکسوس', true)
ON CONFLICT (slug) DO NOTHING;

INSERT INTO public.roles (id, tenant_id, name, description, is_system)
VALUES 
('22222222-2222-2222-2222-222222222222', '11111111-1111-1111-1111-111111111111', 'SuperAdmin', 'مدیر ارشد سامانه با تمام دسترسی‌ها', true),
('44444444-4444-4444-4444-444444444444', '11111111-1111-1111-1111-111111111111', 'Standard User', 'کاربر عادی سیستم', false)
ON CONFLICT DO NOTHING;

INSERT INTO public.users (id, tenant_id, email, display_name, password_hash, is_active)
VALUES ('33333333-3333-3333-3333-333333333333', '11111111-1111-1111-1111-111111111111', 'admin@nexus.local', 'مدیر ارشد سامانه', 'Password@123', true)
ON CONFLICT DO NOTHING;

INSERT INTO public.user_roles (user_id, role_id)
VALUES ('33333333-3333-3333-3333-333333333333', '22222222-2222-2222-2222-222222222222')
ON CONFLICT DO NOTHING;

INSERT INTO public.permissions (id, name, module, description)
VALUES
('p-101', 'Identity.Users.View', 'Identity', 'مشاهده لیست کاربران'),
('p-102', 'Identity.Users.Create', 'Identity', 'ایجاد کاربر جدید'),
('p-103', 'Identity.Users.Edit', 'Identity', 'ویرایش مشخصات کاربران'),
('p-201', 'Identity.Roles.View', 'Identity', 'مشاهده نقش‌ها'),
('p-202', 'Identity.Roles.Manage', 'Identity', 'ایجاد و ویرایش نقش‌ها'),
('p-301', 'Platform.Tenants.View', 'Platform', 'مشاهده سازمان‌ها'),
('p-302', 'Platform.Tenants.Manage', 'Platform', 'مدیریت و ایجاد سازمان‌ها'),
('p-401', 'Platform.AuditLogs.View', 'Platform', 'مشاهده گزارش فعالیت‌ها و لاگ‌ها'),
('p-501', 'Platform.Settings.View', 'Platform', 'مشاهده تنظیمات پلتفرم'),
('p-502', 'Platform.Settings.Manage', 'Platform', 'تغییر تنظیمات سیستم'),
('p-601', 'Notifications.View', 'Notifications', 'مشاهده اعلان‌ها'),
('p-701', 'Tasks.Manage', 'Tasks', 'مدیریت و ایجاد تسک‌ها'),
('p-801', 'Chat.Conversations.Access', 'Chat', 'ارسال و دریافت پیام‌ها در چت'),
('p-901', 'Ticketing.Tickets.Manage', 'Ticketing', 'مدیریت تیکت‌های پشتیبانی'),
('p-1001', 'Events.Calendar.Manage', 'Events', 'مدیریت رویدادها و تقویم')
ON CONFLICT (id) DO NOTHING;
