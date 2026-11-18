import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { fileSize } from '../../features/files/files.api';
import { FileLinks } from './file-links';
import { docColor, docType, waFormat, waKind } from './wa-format';

/** The file part of a message, as the screens have it. */
export interface WaFile {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  isBigVideo?: boolean;
  /** False once the server freed the file after sending: only its card is shown. */
  kept?: boolean;
}

export type WaStatus = 'pending' | 'sent' | 'delivered' | 'read' | 'failed' | null;

/**
 * One sent message drawn the way WhatsApp draws it: green bubble on the chat background, the real photo, the
 * video's first frame with a play button and its length, a document card (type, size), the caption with
 * WhatsApp's formatting, the time and ticks. Used in the composer, templates, occasion settings and sent chats.
 */
@Component({
  selector: 'app-wa-bubble',
  templateUrl: './wa-bubble.html',
  styleUrl: './wa-bubble.css',
})
export class WaBubble {
  private readonly links = inject(FileLinks);

  readonly text = input<string | null>(null);
  readonly file = input<WaFile | null>(null);
  readonly time = input<string | Date | null>(null);
  readonly status = input<WaStatus>(null);
  /** Shown above the bubble, like WhatsApp's day chip ("Preview", "Today"). */
  readonly chip = input<string | null>(null);

  protected readonly url = signal<string | null>(null);
  protected readonly playing = signal(false);
  protected readonly duration = signal<string | null>(null);
  protected readonly broken = signal(false);

  protected readonly kind = computed(() => {
    const f = this.file();
    return f ? waKind(f.contentType, f.isBigVideo === true) : null;
  });
  protected readonly html = computed(() => waFormat(this.text() ?? ''));
  protected readonly clock = computed(() => {
    const t = this.time();
    return new Date(t ?? Date.now()).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', hour12: true }).toUpperCase();
  });

  protected readonly fileSize = fileSize;
  protected readonly docType = docType;
  protected readonly docColor = docColor;

  constructor() {
    effect(() => {
      const f = this.file();
      this.url.set(null);
      this.playing.set(false);
      this.duration.set(null);
      this.broken.set(false);
      const kind = this.kind();
      if (f && f.kept !== false && (kind === 'image' || kind === 'video' || kind === 'audio')) {
        void this.links.urlFor(f.id).then((u) => {
          if (this.file()?.id === f.id) this.url.set(u);
        });
      }
    });
  }

  protected onMeta(video: HTMLVideoElement | HTMLAudioElement): void {
    const s = Math.round(video.duration);
    if (Number.isFinite(s)) this.duration.set(`${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`);
  }

  protected play(video: HTMLVideoElement): void {
    this.playing.set(true);
    video.controls = true;
    void video.play();
  }
}
