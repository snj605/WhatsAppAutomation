/** Shapes returned by the server's /api/v1/auth endpoints. They mirror the C# contracts. */
export interface UserInfo {
  id: string;
  userName: string;
  displayName: string;
  roles: string[];
}

export interface WebSession {
  accessToken: string;
  accessTokenExpiresAt: string;
  user: UserInfo;
}

export interface Me {
  user: UserInfo;
  permissions: string[];
}
