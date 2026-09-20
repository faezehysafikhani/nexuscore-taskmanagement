import React, { useState, useEffect } from 'react';
import { Layout } from './components/Layout';
import { Dashboard } from './pages/Dashboard';
import { Login } from './pages/Login';
import { Users } from './pages/Users';
import { Roles } from './pages/Roles';
import { Permissions } from './pages/Permissions';
import { UserRoles } from './pages/UserRoles';
import { RolePermissions } from './pages/RolePermissions';
import { Tenants } from './pages/Tenants';
import { AuditLogs } from './pages/AuditLogs';
import { TaskManager } from './pages/TaskManager';
import { Events } from './pages/Events';
import { Chat } from './pages/Chat';
import { Ticketing } from './pages/Ticketing';
import { Notifications } from './pages/Notifications';
import { Settings } from './pages/Settings';
import { UserDto, NotificationDto } from './types';
import { AuthTokenStore, api } from './services/api';

export function App() {
  const [currentPage, setCurrentPage] = useState<string>('dashboard');
  const [currentUser, setCurrentUser] = useState<UserDto | null>(null);
  const [unreadNotificationsCount, setUnreadNotificationsCount] = useState(0);

  useEffect(() => {
    checkCurrentUser();
    loadUnreadCount();
  }, []);

  const checkCurrentUser = async () => {
    const token = AuthTokenStore.getAccessToken();
    if (!token) {
      setCurrentPage('login');
      return;
    }
    try {
      const res = await api.get<{ user: UserDto; permissions: string[] }>('/api/identity/auth/me');
      if (res.isSuccess && res.value?.user) {
        setCurrentUser(res.value.user);
        if (res.value.user.id) {
          AuthTokenStore.set(token, undefined, res.value.user.id);
        }
      } else {
        AuthTokenStore.clear();
        setCurrentPage('login');
      }
    } catch {
      AuthTokenStore.clear();
      setCurrentPage('login');
    }
  };

  const loadUnreadCount = async () => {
    try {
      const res = await api.get<NotificationDto[]>('/api/notifications');
      if (res.isSuccess && res.value) {
        const unread = res.value.filter((n) => !n.isRead).length;
        setUnreadNotificationsCount(unread);
      }
    } catch {
      // Ignore
    }
  };

  const handleLogout = () => {
    AuthTokenStore.clear();
    setCurrentUser(null);
    setCurrentPage('login');
  };

  const renderPage = () => {
    if (!currentUser && currentPage !== 'login') {
      return (
        <Login
          onLoginSuccess={(user) => {
            setCurrentUser(user);
            setCurrentPage('dashboard');
          }}
          onNavigate={setCurrentPage}
        />
      );
    }

    switch (currentPage) {
      case 'dashboard':
        return <Dashboard onNavigate={setCurrentPage} />;
      case 'login':
        return (
          <Login
            onLoginSuccess={(user) => {
              setCurrentUser(user);
              setCurrentPage('dashboard');
            }}
            onNavigate={setCurrentPage}
          />
        );
      case 'users':
        return <Users />;
      case 'roles':
        return <Roles />;
      case 'permissions':
        return <Permissions />;
      case 'user-roles':
        return <UserRoles />;
      case 'role-permissions':
        return <RolePermissions />;
      case 'tenants':
        return <Tenants />;
      case 'audit-logs':
        return <AuditLogs />;
      case 'tasks':
        return <TaskManager currentUser={currentUser} />;
      case 'events':
        return <Events />;
      case 'chat':
        return <Chat currentUser={currentUser} />;
      case 'tickets':
        return <Ticketing currentUser={currentUser} />;
      case 'notifications':
        return <Notifications onRefreshUnread={loadUnreadCount} />;
      case 'settings':
        return <Settings />;
      default:
        return <Dashboard onNavigate={setCurrentPage} />;
    }
  };

  return (
    <Layout
      currentPage={currentPage}
      onNavigate={setCurrentPage}
      currentUser={currentUser}
      unreadNotificationsCount={unreadNotificationsCount}
      onLogout={handleLogout}
    >
      {renderPage()}
    </Layout>
  );
}

export default App;
