import { Routes } from '@angular/router';
import { guestGuard, homeRedirect, permissionGuard, signedInGuard, signedInOnlyGuard } from './core/guards/auth.guards';
import { ChatLayout } from './layouts/chat-layout/chat-layout';

/** Pages are loaded only when first visited (loadComponent), which keeps the start-up download small. */
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: 'change-password',
    title: 'Choose your password',
    canActivate: [signedInOnlyGuard],
    loadComponent: () => import('./features/account/change-password.page').then((m) => m.ChangePasswordPage),
  },
  {
    path: '',
    component: ChatLayout,
    canActivate: [signedInGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: homeRedirect },
      {
        path: 'chats',
        title: 'Chats',
        canActivate: [permissionGuard('messages.send')],
        loadComponent: () => import('./features/chats/chats-shell').then((m) => m.ChatsShell),
        children: [
          {
            path: 'new',
            title: 'New message',
            loadComponent: () => import('./features/chats/new-chat.page').then((m) => m.NewChatPage),
          },
          {
            path: 'group/:id',
            title: 'Group',
            data: { kind: 'group' },
            loadComponent: () => import('./features/chats/target-chat.page').then((m) => m.TargetChatPage),
          },
          {
            path: 'group/:id/info',
            title: 'Group info',
            data: { kind: 'group' },
            loadComponent: () => import('./features/chats/chat-info.page').then((m) => m.ChatInfoPage),
          },
          {
            path: 'category/:id',
            title: 'Category',
            data: { kind: 'category' },
            loadComponent: () => import('./features/chats/target-chat.page').then((m) => m.TargetChatPage),
          },
          {
            path: 'category/:id/info',
            title: 'Category info',
            data: { kind: 'category' },
            loadComponent: () => import('./features/chats/chat-info.page').then((m) => m.ChatInfoPage),
          },
          {
            path: ':id',
            title: 'Chat',
            loadComponent: () => import('./features/chats/chat.page').then((m) => m.ChatPage),
          },
        ],
      },
      // Older links.
      { path: 'broadcasts', pathMatch: 'full', redirectTo: 'chats' },
      { path: 'broadcasts/new', redirectTo: 'chats/new' },
      { path: 'broadcasts/:id', redirectTo: 'chats/:id' },
      {
        path: 'home',
        title: 'Overview',
        loadComponent: () => import('./features/home/home.page').then((m) => m.HomePage),
      },
      {
        path: 'templates',
        title: 'Templates',
        canActivate: [permissionGuard('templates.manage')],
        loadComponent: () => import('./features/templates/templates.page').then((m) => m.TemplatesPage),
      },
      {
        path: 'me',
        title: 'Me',
        loadComponent: () => import('./features/me/me.page').then((m) => m.MePage),
      },
      {
        path: 'whatsapp',
        title: 'WhatsApp',
        canActivate: [permissionGuard('whatsapp.link', 'messages.send')],
        loadComponent: () => import('./features/whatsapp/whatsapp.page').then((m) => m.WhatsAppPage),
      },
      {
        path: 'modules',
        title: 'Occasions',
        canActivate: [permissionGuard('modules.manage')],
        loadComponent: () => import('./features/modules/modules.page').then((m) => m.ModulesPage),
      },
      {
        path: 'contact-fields',
        title: 'Contact fields',
        canActivate: [permissionGuard('modules.manage')],
        loadComponent: () => import('./features/contacts/contact-fields.page').then((m) => m.ContactFieldsPage),
      },
      {
        path: 'salutations',
        title: 'Salutations',
        canActivate: [permissionGuard('modules.manage')],
        loadComponent: () => import('./features/contacts/salutations.page').then((m) => m.SalutationsPage),
      },
      {
        path: 'modules/:id',
        title: 'Occasion',
        canActivate: [permissionGuard('modules.manage')],
        loadComponent: () => import('./features/modules/module.page').then((m) => m.ModulePage),
      },
      {
        path: 'sending',
        title: 'Sending',
        canActivate: [permissionGuard('messages.send')],
        loadComponent: () => import('./features/sending/sending.page').then((m) => m.SendingPage),
      },
      {
        path: 'sending/results/:id',
        title: 'Results',
        canActivate: [permissionGuard('messages.send')],
        loadComponent: () => import('./features/sending/results.page').then((m) => m.ResultsPage),
      },
      {
        path: 'notifications',
        title: 'Notifications',
        loadComponent: () => import('./features/notifications/notifications.page').then((m) => m.NotificationsPage),
      },
      // Older links.
      { path: 'birthdays', redirectTo: 'modules' },
      {
        path: 'contacts',
        title: 'Contacts',
        canActivate: [permissionGuard('contacts.view')],
        loadComponent: () => import('./features/contacts/contacts.page').then((m) => m.ContactsPage),
      },
      {
        path: 'contacts/lists',
        title: 'Categories & groups',
        canActivate: [permissionGuard('contacts.view')],
        loadComponent: () => import('./features/contacts/lists.page').then((m) => m.ContactListsPage),
      },
      {
        path: 'contacts/import',
        title: 'Import contacts',
        canActivate: [permissionGuard('contacts.import')],
        loadComponent: () => import('./features/contacts/import.page').then((m) => m.ImportPage),
      },
      {
        path: 'members',
        title: 'Members',
        canActivate: [permissionGuard('members.manage')],
        loadComponent: () => import('./features/members/members.page').then((m) => m.MembersPage),
      },
      {
        path: 'platform/dashboard',
        title: 'Dashboard',
        canActivate: [permissionGuard('platform.tenants')],
        loadComponent: () => import('./features/platform/dashboard.page').then((m) => m.DashboardPage),
      },
      {
        path: 'platform/tenants',
        title: 'Tenants',
        canActivate: [permissionGuard('platform.tenants')],
        loadComponent: () => import('./features/platform/tenants.page').then((m) => m.TenantsPage),
      },
      {
        path: 'account/password',
        title: 'Change password',
        loadComponent: () => import('./features/account/change-password.page').then((m) => m.ChangePasswordPage),
      },
      // Older links.
      { path: 'settings/users', redirectTo: 'members' },
    ],
  },
  { path: '**', redirectTo: '' },
];
