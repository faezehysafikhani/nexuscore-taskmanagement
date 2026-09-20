export interface LoginRequest {
  email: string;
  password?: string;
  tenantSlug?: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  accessTokenExpiresAtUtc: string;
  user: UserDto;
}

export interface UserDto {
  id: string;
  tenantId: string;
  email: string;
  displayName: string;
  isActive: boolean;
  lastLoginAtUtc?: string | null;
  createdAtUtc?: string | null;
  roles: string[];
}

export interface CreateUserRequest {
  tenantId: string;
  email: string;
  displayName: string;
  password?: string;
  isActive?: boolean;
}

export interface UpdateUserRequest {
  displayName: string;
  isActive: boolean;
}

export interface AssignUserRolesRequest {
  roleIds: string[];
}

export interface RoleDto {
  id: string;
  tenantId: string;
  name: string;
  description?: string | null;
  isSystem: boolean;
  permissions: string[];
}

export interface CreateRoleRequest {
  tenantId: string;
  name: string;
  description?: string | null;
}

export interface AssignRolePermissionsRequest {
  permissionIds: string[];
}

export interface PermissionDto {
  id: string;
  name: string;
  module: string;
  description: string;
}

export interface PermissionGroupDto {
  module: string;
  permissions: PermissionDto[];
}

export interface TenantDto {
  id: string;
  name: string;
  slug: string;
  description?: string | null;
  isActive: boolean;
}

export interface CreateTenantRequest {
  name: string;
  slug: string;
  description?: string | null;
}

export interface AuditLogDto {
  id: string;
  tenantId?: string | null;
  userId?: string | null;
  action: string;
  entityName?: string | null;
  entityId?: string | null;
  details?: string | null;
  ipAddress?: string | null;
  occurredAtUtc: string;
}

export interface SystemSettingDto {
  id: string;
  tenantId?: string | null;
  key: string;
  value: string;
  group: string;
}

export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ApiResult<T = any> {
  isSuccess: boolean;
  value?: T | null;
  error?: string | null;
}

// Additional Modules: Task Manager, Chat, Ticketing, Notifications
// "Tasks" in this platform are Nexus.Actions items - matches Nexus.Actions.Application.Dtos.ActionItemDto
export interface TaskDto {
  id: string;
  tenantId: string;
  title: string;
  description?: string | null;
  ownerUserId?: string | null;
  responsibleUserId?: string | null;
  status: 'Open' | 'InProgress' | 'Completed' | 'Cancelled';
  organizationUnitId: string;
  workCalendarId: string;
  projectId?: string | null;
  startDate?: string | null;
  endDate?: string | null;
}

// Matches Nexus.Organization.Application.Dtos.OrganizationUnitDto
export interface OrganizationUnitDto {
  id: string;
  tenantId: string;
  name: string;
  code: string;
  parentId?: string | null;
  managerUserId?: string | null;
  isActive: boolean;
}

// Matches Nexus.Calendar.Application.Dtos.WorkCalendarDto
export interface WorkCalendarDto {
  id: string;
  tenantId: string;
  name: string;
  description?: string | null;
  workingDays: number;
  isDefault: boolean;
}

export interface ConversationDto {
  id: string;
  tenantId: string;
  title: string;
  isGroup: boolean;
  lastMessage?: string | null;
  lastMessageAtUtc?: string | null;
  participants: {
    userId: string;
    displayName: string;
    roleBadge?: string;
    email?: string;
  }[];
  otherUser?: {
    id: string;
    displayName: string;
    roleBadge: string;
    roleCategory: 'superadmin' | 'manager' | 'support' | 'customer';
    email: string;
    isOnline: boolean;
  };
  unreadCount?: number;
}

export interface ChatContactDto {
  id: string;
  tenantId: string;
  displayName: string;
  email: string;
  roleName: string;
  roleCategory: 'superadmin' | 'manager' | 'support' | 'customer';
  roleBadgePersian: string;
  isOnline: boolean;
  existingConversationId?: string | null;
  unreadCount?: number;
}

export interface MessageDto {
  id: string;
  conversationId: string;
  senderUserId: string;
  senderDisplayName: string;
  content: string;
  sentAtUtc: string;
  isRead?: boolean;
}

// Matches Ticketing.Application.Common.Dtos.TicketDto (list view - no description/comments)
export interface TicketDto {
  id: string;
  number: string;
  title: string;
  status: string;
  priority: string;
  assignedToUserId?: string | null;
  createdAt: string;
}

// Matches Ticketing.Application.Common.Dtos.TicketCommentDto
export interface TicketCommentDto {
  id: string;
  userId?: string | null;
  text: string;
  createdAt: string;
}

// Matches Ticketing.Application.Common.Dtos.TicketDetailsDto (GET /api/tickets/{id})
export interface TicketDetailsDto extends TicketDto {
  description: string;
  createdByUserId?: string | null;
  comments: TicketCommentDto[];
}

export interface NotificationDto {
  id: string;
  userId: string;
  title: string;
  message: string;
  type: 'Info' | 'Warning' | 'Success' | 'Alert';
  isRead: boolean;
  createdAtUtc: string;
  link?: string | null;
}

// Events Module
export interface EventDto {
  id: string;
  tenantId: string;
  userId: string;
  title: string;
  description?: string | null;
  startAtUtc: string;
  endAtUtc?: string | null;
  isCompleted: boolean;
  reminderMinutesBefore?: number | null;
  reminderSent: boolean;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface CreateEventRequest {
  title: string;
  description?: string;
  startAtUtc: string;
  endAtUtc?: string;
  reminderMinutesBefore?: number | null;
}

export interface UpdateEventRequest {
  title?: string;
  description?: string;
  startAtUtc?: string;
  endAtUtc?: string;
  isCompleted?: boolean;
  reminderMinutesBefore?: number | null;
}
