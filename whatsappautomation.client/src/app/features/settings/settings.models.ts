/** Shapes of the users and roles API. They mirror the C# contracts in Api/Contracts/AccessContracts.cs. */
export interface Permission {
  name: string;
  group: string;
  description: string;
}

export interface Role {
  id: string;
  name: string;
  description: string | null;
  isSystem: boolean;
  permissions: string[];
  userCount: number;
}

export interface SaveRole {
  name: string;
  description: string | null;
  permissions: string[];
}

export interface RoleRef {
  id: string;
  name: string;
}

export interface UserDetails {
  id: string;
  userName: string;
  displayName: string;
  isActive: boolean;
  roles: RoleRef[];
  createdAt: string;
}

export interface CreateUser {
  userName: string;
  displayName: string;
  password: string;
  roleIds: string[];
}
