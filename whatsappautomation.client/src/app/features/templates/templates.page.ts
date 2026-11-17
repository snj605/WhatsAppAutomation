import { Component, ElementRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { MORE_INSERTS, NAME_PLACEHOLDER } from '../chats/send-templates';
import { MessageTemplate, TemplatesApi } from './templates.api';

/** Templates: the user's own saved messages, ready to pick when sending. Add, edit and delete them here. */
@Component({
  selector: 'app-templates-page',
  imports: [RouterLink],
  templateUrl: './templates.page.html',
  styleUrl: './templates.page.css',
})
export class TemplatesPage implements OnInit {
  private readonly api = inject(TemplatesApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly box = viewChild<ElementRef<HTMLTextAreaElement>>('box');

  protected readonly moreInserts = MORE_INSERTS;
  protected readonly loading = signal(true);
  protected readonly templates = signal<MessageTemplate[]>([]);

  /** The template being edited: 'new', an existing one, or null when the form is closed. */
  protected readonly editing = signal<MessageTemplate | 'new' | null>(null);
  protected readonly name = signal('');
  protected readonly text = signal('');
  protected readonly saving = signal(false);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected startNew(): void {
    this.editing.set('new');
    this.name.set('');
    this.text.set('');
  }

  protected edit(t: MessageTemplate): void {
    this.editing.set(t);
    this.name.set(t.name);
    this.text.set(t.text);
  }

  protected cancel(): void {
    this.editing.set(null);
  }

  protected addName(): void {
    this.insert(NAME_PLACEHOLDER);
  }

  protected insert(value: string): void {
    const el = this.box()?.nativeElement;
    const current = this.text();
    const start = el?.selectionStart ?? current.length;
    const end = el?.selectionEnd ?? current.length;
    const piece = (start > 0 && !/\s$/.test(current.slice(0, start)) ? ' ' : '') + value;
    this.text.set(current.slice(0, start) + piece + current.slice(end));
    setTimeout(() => {
      el?.focus();
      el?.setSelectionRange(start + piece.length, start + piece.length);
    });
  }

  protected async save(): Promise<void> {
    const editing = this.editing();
    if (!editing || !this.name().trim() || !this.text().trim()) return;
    this.saving.set(true);
    try {
      await this.api.save(editing === 'new' ? null : editing.id, this.name().trim(), this.text().trim());
      this.toast.add({ severity: 'success', summary: 'Template saved' });
      this.editing.set(null);
      await this.load();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
    } finally {
      this.saving.set(false);
    }
  }

  protected remove(t: MessageTemplate): void {
    this.confirm.confirm({
      header: 'Delete this template?',
      message: `"${t.name}" will be removed. Messages already sent are not affected.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        try {
          await this.api.remove(t.id);
          await this.load();
        } catch (e) {
          this.toast.add({ severity: 'error', summary: 'Not deleted', detail: problemMessage(e), life: 6000 });
        }
      },
    });
  }

  /** Placeholders shown as words in the list, e.g. "[Name]". */
  protected friendly(text: string): string {
    return text
      .replace(/\{\{\s*name_gu\s*\}\}/gi, '[Gujarati name]')
      .replace(/\{\{\s*name_hi\s*\}\}/gi, '[Hindi name]')
      .replace(/\{\{\s*name_en\s*\}\}/gi, '[English name]')
      .replace(/\{\{\s*name\s*\}\}/gi, '[Name]')
      .replace(/\{\{\s*salutation\s*\}\}/gi, '[Salutation]');
  }

  private async load(): Promise<void> {
    try {
      this.templates.set(await this.api.list());
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load templates', detail: problemMessage(e), life: 6000 });
    } finally {
      this.loading.set(false);
    }
  }
}
