import { Routes } from '@angular/router';
import { guestGuard, permissionGuard, signedInGuard } from './core/guards/auth.guards';
import { ChatLayout } from './layouts/chat-layout/chat-layout';

/** Pages are loaded only when first visited (loadComponent), which keeps the start-up download small. */
export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login/login.page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: ChatLayout,
    canActivate: [signedInGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'chats' },
      {
        path: 'chats',
        title: 'Chats',
        canActivate: [permissionGuard('broadcasts.send')],
        loadComponent: () => import('./features/chats/chats-shell').then((m) => m.ChatsShell),
        children: [
          {
            path: 'new',
            title: 'New message',
            loadComponent: () => import('./features/chats/new-chat.page').then((m) => m.NewChatPage),
          },
          {
            path: 'birthdays',
            title: 'Birthdays today',
            loadComponent: () => import('./features/chats/birthdays-chat.page').then((m) => m.BirthdaysChatPage),
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
        canActivate: [permissionGuard('broadcasts.send')],
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
        path: 'birthdays',
        title: 'Birthdays',
        canActivate: [permissionGuard('contacts.view')],
        loadComponent: () => import('./features/birthdays/birthdays.page').then((m) => m.BirthdaysPage),
      },
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
        canActivate: [permissionGuard('contacts.manage')],
        loadComponent: () => import('./features/contacts/import.page').then((m) => m.ImportPage),
      },
      {
        path: 'settings/users',
        title: 'Users',
        canActivate: [permissionGuard('users.view')],
        loadComponent: () => import('./features/settings/users/users.page').then((m) => m.UsersPage),
      },
      {
        path: 'settings/roles',
        title: 'Roles',
        canActivate: [permissionGuard('roles.view')],
        loadComponent: () => import('./features/settings/roles/roles.page').then((m) => m.RolesPage),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
