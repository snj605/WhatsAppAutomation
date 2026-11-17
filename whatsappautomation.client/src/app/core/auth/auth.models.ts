/** Shapes returned by the server's /api/v1/auth endpoints. They mirror the C# contracts. */
export type UserKind = 'platform_admin' | 'tenant_admin' | 'member';

export interface UserInfo {
  id: string;
  userName: string;
  displayName: string;
  kind: UserKind;
  /** The tenant's name; null for the platform admin. */
  tenantName: string | null;
  /** An admin chose the password: nothing else works until the user chooses their own. */
  mustChangePassword: boolean;
}

/** How each kind is named on screen. */
export const KIND_LABELS: Record<UserKind, string> = {
  platform_admin: 'Platform admin',
  tenant_admin: 'Admin',
  member: 'Member',
};

export interface WebSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: UserInfo;
}

export interface Me {
  user: UserInfo;
  permissions: string[];
}
