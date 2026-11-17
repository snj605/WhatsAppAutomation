import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Shapes of /api/v1/platform/tenants. They mirror Api/Contracts/AccessContracts.cs. Counts only, by design. */
export interface Tenant {
  id: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  users: number;
  admins: number;
}

export interface TenantAdmin {
  id: string;
  userName: string;
  displayName: string;
  isActive: boolean;
  createdAt: string;
}

export interface CreateTenant {
  name: string;
  adminUserName: string;
  adminDisplayName: string;
  adminPassword: string;
}

const BASE = '/api/v1/platform/tenants';

/** Calls the platform admin uses: tenants and their admins. */
@Injectable({ providedIn: 'root' })
export class TenantsApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<Tenant[]>(BASE));
  create = (tenant: CreateTenant) => firstValueFrom(this.http.post<{ id: string }>(BASE, tenant));
  rename = (id: string, name: string) => firstValueFrom(this.http.put<void>(`${BASE}/${id}`, { name }));
  setActive = (id: string, isActive: boolean) => firstValueFrom(this.http.put<void>(`${BASE}/${id}/active`, { isActive }));
  /** Only a turned-off tenant; removes all its people and their data. */
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`${BASE}/${id}`));

  admins = (id: string) => firstValueFrom(this.http.get<TenantAdmin[]>(`${BASE}/${id}/admins`));
  addAdmin = (id: string, admin: { userName: string; displayName: string; password: string }) =>
    firstValueFrom(this.http.post<{ id: string }>(`${BASE}/${id}/admins`, admin));
  setAdminActive = (id: string, adminId: string, isActive: boolean) =>
    firstValueFrom(this.http.put<void>(`${BASE}/${id}/admins/${adminId}/active`, { isActive }));
  resetAdminPassword = (id: string, adminId: string, password: string) =>
    firstValueFrom(this.http.put<void>(`${BASE}/${id}/admins/${adminId}/password`, { password }));
  removeAdmin = (id: string, adminId: string) => firstValueFrom(this.http.delete<void>(`${BASE}/${id}/admins/${adminId}`));
}
