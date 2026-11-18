import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AutoCompleteModule } from 'primeng/autocomplete';
import { BroadcastsApi } from '../../features/broadcasts/broadcasts.api';
import { ContactsApi } from '../../features/contacts/contacts.api';
import { Contact, displayName } from '../../features/contacts/contacts.models';
import { WaBubble, WaFile } from './wa-bubble';

/**
 * A text (and file) as WhatsApp shows it, for a person the user picks: their own name, second name, custom
 * fields and language, from the server's own preview. Without a person, an example with sample names.
 */
@Component({
  selector: 'app-wa-person-preview',
  imports: [FormsModule, AutoCompleteModule, WaBubble],
  template: `
    <div class="pick">
      <span>Preview for</span>
      <p-autocomplete [ngModel]="person()" (ngModelChange)="pick($event)" [suggestions]="hits()" (completeMethod)="find($event.query)"
        [optionLabel]="'label'" placeholder="Type a contact's name" [showClear]="true" (onClear)="pick(null)" appendTo="body"
        [delay]="300" ariaLabel="Preview for a contact" styleClass="pick-input" />
    </div>
    @if (skip()) {
      <p class="skip"><i class="pi pi-exclamation-triangle" aria-hidden="true"></i> {{ person()?.label }} is left out: {{ skip() }}</p>
    } @else {
      <app-wa-bubble [text]="shown()" [file]="file()" status="read" [chip]="person() ? person()!.label : 'Example'" />
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 8px; }
    .pick { display: flex; align-items: center; gap: 8px; font-size: 13px; color: var(--ui-text-2); }
    :host ::ng-deep .pick-input { flex: 1; min-width: 0; }
    .skip { margin: 0; font-size: 13px; color: #b54708; background: var(--ui-warn-soft); padding: 8px 10px; border-radius: 10px; }
  `,
})
export class WaPersonPreview {
  private readonly contacts = inject(ContactsApi);
  private readonly broadcasts = inject(BroadcastsApi);

  readonly text = input<string>('');
  readonly file = input<WaFile | null>(null);
  /** Occasion wishes fill {{for_name}} from this occasion. */
  readonly moduleId = input<string | null>(null);

  protected readonly person = signal<{ label: string; id: string } | null>(null);
  protected readonly hits = signal<{ label: string; id: string }[]>([]);
  protected readonly shown = signal('');
  protected readonly skip = signal<string | null>(null);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private seq = 0;

  constructor() {
    effect(() => {
      const text = this.text();
      const file = this.file();
      const moduleId = this.moduleId();
      const person = this.person();
      untracked(() => {
        clearTimeout(this.timer);
        if (!person || !text.trim()) {
          this.skip.set(null);
          this.shown.set(sample(text));
          return;
        }

        this.timer = setTimeout(() => void this.load(text, file, moduleId, person.id), 400);
      });
    });
  }

  protected async find(query: string): Promise<void> {
    try {
      const page = await this.contacts.contacts({ search: query, page: 1, pageSize: 8 });
      this.hits.set(page.items.map((c: Contact) => ({ label: displayName(c), id: c.id })));
    } catch {
      this.hits.set([]);
    }
  }

  protected pick(value: { label: string; id: string } | string | null): void {
    this.person.set(value && typeof value === 'object' ? value : null);
  }

  private async load(text: string, file: WaFile | null, moduleId: string | null, contactId: string): Promise<void> {
    const seq = ++this.seq;
    try {
      const preview = await this.broadcasts.preview(
        { name: 'Preview', contactIds: [contactId], categoryIds: [], groupIds: [], textEn: null, textHi: null, textGu: null, text, attachmentId: file?.id ?? null, moduleId, sendVideoAsDocument: file?.isBigVideo === true },
        contactId,
      );
      if (seq !== this.seq) return;
      this.skip.set(preview.person?.skipReason ?? null);
      this.shown.set(preview.person?.text ?? sample(text));
    } catch {
      if (seq === this.seq) {
        this.skip.set(null);
        this.shown.set(sample(text));
      }
    }
  }
}

/** The text with sample values, so the user sees spacing, new lines and links as people get them. */
export function sample(text: string): string {
  return text
    .replace(/\{([^{}|]*)\|[^{}]*\}/g, '$1')
    .replace(/\{\{\s*(name|name_en|name_hi|name_gu)\s*\}\}/g, 'Ramesh Patel')
    .replace(/\{\{\s*for_name\s*\}\}/g, 'Late Shri Mohanbhai')
    .replace(/\{\{\s*salutation\s*\}\}\s*/g, '')
    .replace(/\{\{\s*([a-z0-9_]+)\s*\}\}/g, '[$1]')
    .replace(/ {2,}/g, ' ');
}
