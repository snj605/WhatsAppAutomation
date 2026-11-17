import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CreateMember, Member, UpdateMember } from './members.models';

/** Calls to /api/v1/members: the tenant admin manages the people of their own tenant. */
@Injectable({ providedIn: 'root' })
export class MembersApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<Member[]>('/api/v1/members'));
  create = (member: CreateMember) => firstValueFrom(this.http.post<{ id: string }>('/api/v1/members', member));
  update = (id: string, member: UpdateMember) => firstValueFrom(this.http.put<void>(`/api/v1/members/${id}`, member));
  setActive = (id: string, isActive: boolean) =>
    firstValueFrom(this.http.put<void>(`/api/v1/members/${id}/active`, { isActive }));
  /** Sets a new password; they choose their own at the next sign-in. */
  resetPassword = (id: string, password: string) =>
    firstValueFrom(this.http.put<void>(`/api/v1/members/${id}/password`, { password }));
  /** Deletes the person and everything that is theirs. Cannot be undone. */
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/members/${id}`));
}
