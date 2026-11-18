import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BroadcastDetails, BroadcastInput, BroadcastPreview, BroadcastSummary, Pacing } from './broadcasts.models';

/** Calls to the signed-in user's own broadcasts. */
@Injectable({ providedIn: 'root' })
export class BroadcastsApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<BroadcastSummary[]>('/api/v1/broadcasts'));
  get = (id: string) => firstValueFrom(this.http.get<BroadcastDetails>(`/api/v1/broadcasts/${id}`));
  /** With forContactId: also exactly what that person gets. */
  preview = (input: BroadcastInput, forContactId: string | null = null) =>
    firstValueFrom(
      this.http.post<BroadcastPreview>('/api/v1/broadcasts/preview', input, forContactId ? { params: { for: forContactId } } : {}),
    );
  create = (input: BroadcastInput) => firstValueFrom(this.http.post<BroadcastSummary>('/api/v1/broadcasts', input));
  change = (id: string, action: 'pause' | 'resume' | 'cancel') =>
    firstValueFrom(this.http.post<BroadcastSummary>(`/api/v1/broadcasts/${id}/${action}`, null));
  pacing = () => firstValueFrom(this.http.get<Pacing>('/api/v1/broadcasts/pacing'));
}
