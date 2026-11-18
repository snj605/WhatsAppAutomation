import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** One of the user's salutations (master data), in English and Gujarati: GET /api/v1/salutations. */
export interface Salutation {
  id: string;
  nameEn: string;
  nameGu: string;
  /** "Shri / શ્રી", as pick-lists and Excel show it. */
  label: string;
  contactCount: number;
}

/** The user's salutations, shared by the contact form and the setup page. */
@Injectable({ providedIn: 'root' })
export class SalutationsStore {
  private readonly http = inject(HttpClient);
  readonly list = signal<Salutation[]>([]);
  readonly loaded = signal(false);

  async reload(): Promise<void> {
    try {
      this.list.set(await firstValueFrom(this.http.get<Salutation[]>('/api/v1/salutations')));
    } finally {
      this.loaded.set(true);
    }
  }

  save = (id: string | null, nameEn: string, nameGu: string) =>
    firstValueFrom(
      id
        ? this.http.put<Salutation>(`/api/v1/salutations/${id}`, { nameEn, nameGu })
        : this.http.post<Salutation>('/api/v1/salutations', { nameEn, nameGu }),
    );
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/salutations/${id}`));
}
