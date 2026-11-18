import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Shapes of /api/v1/platform/dashboard and /audit. They mirror Application/Features/Platform. Counts only, by design. */
export interface DashboardTenant {
  id: string;
  name: string;
  isActive: boolean;
  users: number;
  numbersConnected: number;
  numbers: number;
  sentToday: number;
  failedToday: number;
  modules: string[];
}

export interface NumberAlert {
  tenantName: string;
  userName: string;
  /** Last digits only. */
  numberEnd: string | null;
  status: string;
  since: string | null;
}

export interface AuditEntry {
  at: string;
  actorName: string;
  tenantName: string | null;
  action: string;
  text: string;
}

export interface Dashboard {
  tenants: number;
  activeTenants: number;
  users: number;
  numbersConnected: number;
  numbers: number;
  messagesToday: number;
  failedToday: number;
  tenantRows: DashboardTenant[];
  alerts: NumberAlert[];
  recentActivity: AuditEntry[];
}

@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);

  dashboard = () => firstValueFrom(this.http.get<Dashboard>('/api/v1/platform/dashboard'));
  audit = (page: number, pageSize = 50) =>
    firstValueFrom(this.http.get<{ items: AuditEntry[]; total: number }>('/api/v1/platform/audit', { params: { page, pageSize } }));
}
