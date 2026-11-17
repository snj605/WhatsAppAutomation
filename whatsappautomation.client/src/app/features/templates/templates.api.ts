import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** One of the user's saved messages (GET /api/v1/templates). */
export interface MessageTemplate {
  id: string;
  name: string;
  text: string;
  updatedAt: string;
}

@Injectable({ providedIn: 'root' })
export class TemplatesApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<MessageTemplate[]>('/api/v1/templates'));
  save = (id: string | null, name: string, text: string) =>
    firstValueFrom(
      id
        ? this.http.put<MessageTemplate>(`/api/v1/templates/${id}`, { name, text })
        : this.http.post<MessageTemplate>('/api/v1/templates', { name, text }),
    );
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/templates/${id}`));
}
