import { Component, ElementRef, inject, input, OnInit, output, signal, viewChild } from '@angular/core';
import { MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { fileIcon, fileProblem, FilesApi, fileSize, UploadedFile } from '../files/files.api';
import { MessageTemplate, TemplatesApi } from '../templates/templates.api';
import { ComposedMessage } from './send-flow';
import { MORE_INSERTS, NAME_PLACEHOLDER, SEND_TEMPLATES } from './send-templates';

/**
 * The message box at the bottom of a group, category or Birthdays chat, like WhatsApp: type, tap "Name" to put each
 * person's name in, pick a saved template, attach a file, and send.
 */
@Component({
  selector: 'app-message-bar',
  templateUrl: './message-bar.html',
  styleUrl: './message-bar.css',
})
export class MessageBar implements OnInit {
  private readonly templatesApi = inject(TemplatesApi);
  private readonly filesApi = inject(FilesApi);
  private readonly toast = inject(MessageService);
  private readonly box = viewChild<ElementRef<HTMLTextAreaElement>>('box');

  /** Text to start with (for example a birthday wish). */
  readonly initialText = input('');
  readonly placeholder = input('Type a message');
  /** Disables sending (for example while nobody is selected). */
  readonly disabled = input(false);
  readonly busy = input(false);
  readonly send = output<ComposedMessage>();

  protected readonly text = signal('');
  protected readonly panel = signal<'none' | 'insert' | 'templates'>('none');
  protected readonly myTemplates = signal<MessageTemplate[]>([]);
  protected readonly suggestions = SEND_TEMPLATES;
  protected readonly inserts = MORE_INSERTS;
  protected readonly attachment = signal<UploadedFile | null>(null);
  protected readonly uploading = signal(false);
  protected readonly fileIcon = fileIcon;
  protected readonly fileSize = fileSize;

  ngOnInit(): void {
    this.text.set(this.initialText());
    this.templatesApi
      .list()
      .then((list) => this.myTemplates.set(list))
      .catch(() => undefined);
  }

  protected togglePanel(panel: 'insert' | 'templates'): void {
    this.panel.set(this.panel() === panel ? 'none' : panel);
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
    this.panel.set('none');
    setTimeout(() => {
      el?.focus();
      el?.setSelectionRange(start + piece.length, start + piece.length);
    });
  }

  protected useTemplate(text: string): void {
    this.text.set(text);
    this.panel.set('none');
    setTimeout(() => this.box()?.nativeElement.focus());
  }

  protected async attach(picker: HTMLInputElement): Promise<void> {
    const file = picker.files?.[0];
    picker.value = '';
    if (!file) return;
    const problem = fileProblem(file);
    if (problem) {
      this.toast.add({ severity: 'warn', summary: 'Cannot attach this file', detail: problem, life: 6000 });
      return;
    }

    this.uploading.set(true);
    try {
      this.attachment.set(await this.filesApi.upload(file));
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not attached', detail: problemMessage(e), life: 6000 });
    } finally {
      this.uploading.set(false);
    }
  }

  protected submit(): void {
    const text = this.text().trim();
    if (!text || this.disabled() || this.busy()) return;
    this.send.emit({ text, attachmentId: this.attachment()?.id ?? null });
  }

  /** Called by the chat after a successful send. */
  clear(): void {
    this.text.set('');
    this.attachment.set(null);
  }
}
