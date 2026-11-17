import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CreateUser, Permission, Role, SaveRole, UserDetails } from './settings.models';

/** Calls to /api/v1/users, /roles and /permissions. Pages use this instead of HttpClient directly. */
@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  permissions = () => firstValueFrom(this.http.get<Permission[]>('/api/v1/permissions'));
  roles = () => firstValueFrom(this.http.get<Role[]>('/api/v1/roles'));
  createRole = (role: SaveRole) => firstValueFrom(this.http.post<{ id: string }>('/api/v1/roles', role));
  updateRole = (id: string, role: SaveRole) => firstValueFrom(this.http.put<void>(`/api/v1/roles/${id}`, role));
  deleteRole = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/roles/${id}`));

  users = () => firstValueFrom(this.http.get<UserDetails[]>('/api/v1/users'));
  createUser = (user: CreateUser) => firstValueFrom(this.http.post<{ id: string }>('/api/v1/users', user));
  setUserRoles = (id: string, roleIds: string[]) =>
    firstValueFrom(this.http.put<void>(`/api/v1/users/${id}/roles`, { roleIds }));
  setUserActive = (id: string, isActive: boolean) =>
    firstValueFrom(this.http.put<void>(`/api/v1/users/${id}/active`, { isActive }));
  /** Deletes the user and everything that is theirs. Cannot be undone. */
  deleteUser = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/users/${id}`));
}
