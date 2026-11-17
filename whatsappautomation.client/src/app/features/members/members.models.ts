import { ConnectionStatus } from '../whatsapp/whatsapp.models';

/** Shapes of /api/v1/members. They mirror MemberResponse and the requests in Api/Contracts/AccessContracts.cs. */
export interface Member {
  id: string;
  userName: string;
  displayName: string;
  kind: 'tenant_admin' | 'member';
  isActive: boolean;
  switches: string[];
  whatsAppStatus: ConnectionStatus;
  whatsAppNumber: string | null;
  createdAt: string;
}

export interface CreateMember {
  userName: string;
  displayName: string;
  password: string;
  isAdmin: boolean;
  switches: string[];
}

export interface UpdateMember {
  displayName: string;
  isAdmin: boolean;
  switches: string[];
}

export interface SwitchInfo {
  permission: string;
  label: string;
  sub: string;
  icon: string;
}

/** The switches a tenant admin can turn on for a member, in the order the server lists them (UserKinds.MemberSwitches). */
export const MEMBER_SWITCHES: SwitchInfo[] = [
  { permission: 'modules.manage', label: 'Create own modules', sub: 'Birthday and other occasions, sent at a set time', icon: 'pi pi-gift' },
  { permission: 'broadcasts.send', label: 'Broadcasts', sub: 'Send to their own groups and lists', icon: 'pi pi-megaphone' },
  { permission: 'contacts.import', label: 'Import from Excel', sub: 'Add many contacts at once', icon: 'pi pi-file-import' },
  { permission: 'templates.manage', label: 'Templates', sub: 'Make and change message templates', icon: 'pi pi-bookmark' },
];
