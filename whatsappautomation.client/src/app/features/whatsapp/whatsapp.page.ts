import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { WhatsAppLiveService } from '../../core/realtime/whatsapp-live.service';
import { WhatsAppApi } from './whatsapp.api';
import { WhatsAppGate } from './whatsapp-gate';
import { LinkMethod, MessageStatus, OutgoingMessage, WhatsAppAccount } from './whatsapp.models';
import { connectionStatus, messageTick } from './whatsapp.status';

const REASONS: Record<string, string> = {
  link_timeout: 'Linking timed out after 3 minutes. Start again when you are ready.',
  number_in_use: 'This WhatsApp number is already linked by another user on the platform, so it was logged out here. Link a different number.',
  link_failed: 'Linking did not finish. Try again.',
  cannot_reach_whatsapp:
    'The app could not reach WhatsApp. Check that this computer is online and that a firewall or proxy is not blocking WhatsApp, then try again.',
  pairing_code_failed: 'WhatsApp did not give a phone code. Check the number, or use the QR code instead.',
  session_invalid: 'The saved login stopped working. Link the number again.',
  unlinked: 'The number was unlinked.',
};

const LINK_SECONDS = 180;

/** WhatsApp: link this user's number (QR or phone code), see its status, and send a test message. */
@Component({
  selector: 'app-whatsapp-page',
  imports: [DatePipe, ReactiveFormsModule, ButtonModule, IconFieldModule, InputIconModule, InputTextModule, MessageModule, TableModule, TagModule, TextareaModule],
  templateUrl: './whatsapp.page.html',
  styleUrl: './whatsapp.page.css',
})
export class WhatsAppPage implements OnInit, OnDestroy {
  private readonly api = inject(WhatsAppApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(AuthService);
  protected readonly live = inject(WhatsAppLiveService);
  private readonly gate = inject(WhatsAppGate);

  readonly account = signal<WhatsAppAccount | null>(null);
  readonly messages = signal<OutgoingMessage[]>([]);
  readonly busy = signal(false);
  readonly sending = signal(false);

  /** Linking in progress on this screen. */
  readonly method = signal<LinkMethod | null>(null);
  readonly qrImage = signal<string | null>(null);
  readonly pairingCode = signal<string | null>(null);
  readonly secondsLeft = signal(0);
  private countdown: ReturnType<typeof setInterval> | null = null;

  readonly canLink = computed(() => {
    this.auth.user();
    return this.auth.can('whatsapp.link');
  });
  readonly canSend = computed(() => {
    this.auth.user();
    return this.auth.can('messages.send');
  });

  readonly status = computed(() => connectionStatus(this.account()?.status));
  readonly reason = computed(() => {
    const reason = this.account()?.statusReason;
    return reason ? REASONS[reason] ?? null : null;
  });
  readonly isConnected = computed(() => this.account()?.status === 'connected');
  readonly isLinking = computed(() => this.account()?.status === 'linking');
  /** States where a number is (still) attached and must be unlinked before linking again. */
  readonly hasNumber = computed(() =>
    ['connected', 'connecting', 'reconnecting', 'restricted'].includes(this.account()?.status ?? ''),
  );

  readonly fb = inject(NonNullableFormBuilder);
  readonly codeForm = this.fb.group({ phoneNumber: ['', [Validators.required, Validators.maxLength(30)]] });
  readonly sendForm = this.fb.group({
    to: ['', [Validators.required, Validators.maxLength(30)]],
    text: ['', [Validators.required, Validators.maxLength(4096)]],
  });

  ngOnInit(): void {
    this.live.qr$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((qr) => void this.showQr(qr));
    this.live.pairingCode$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((code) => this.pairingCode.set(code));
    this.live.account$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((a) => {
      if (a.status === 'connected' && a.statusReason === 'linked') {
        this.toast.add({ severity: 'success', summary: 'WhatsApp linked', detail: a.phoneNumber ?? undefined });
      }

      this.onAccount(a);
    });
    this.live.message$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((m) => this.upsert(m));
    void this.live.start();
    void this.reload();
  }

  ngOnDestroy(): void {
    this.stopCountdown();
    void this.live.stop();
  }

  async reload(): Promise<void> {
    try {
      this.onAccount(await this.api.account());
      if (this.canSend()) {
        this.messages.set(await this.api.messages());
      }
    } catch (error) {
      this.toast.add({ severity: 'error', summary: 'Could not load WhatsApp', detail: problemMessage(error) });
    }
  }

  async link(method: LinkMethod): Promise<void> {
    if (method === 'code' && this.codeForm.invalid) {
      this.codeForm.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.qrImage.set(null);
    this.pairingCode.set(null);
    try {
      const phone = method === 'code' ? this.codeForm.getRawValue().phoneNumber : null;
      this.method.set(method);
      this.onAccount(await this.api.link(method, phone));
      this.startCountdown();
    } catch (error) {
      this.method.set(null);
      this.toast.add({ severity: 'error', summary: 'Could not start linking', detail: problemMessage(error) });
    } finally {
      this.busy.set(false);
    }
  }

  cancelLink(): void {
    void this.doUnlink('Linking cancelled');
  }

  unlink(): void {
    this.confirm.confirm({
      header: 'Unlink this number?',
      message: 'The app stops sending from it, and it disappears from Linked devices on the phone. You can link again later.',
      acceptLabel: 'Unlink',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', text: true },
      accept: () => void this.doUnlink('Number unlinked'),
    });
  }

  async send(): Promise<void> {
    if (this.sendForm.invalid) {
      this.sendForm.markAllAsTouched();
      return;
    }

    this.sending.set(true);
    const { to, text } = this.sendForm.getRawValue();
    try {
      this.upsert(await this.api.send(to, text));
      this.sendForm.controls.text.reset();
      this.toast.add({ severity: 'success', summary: 'Message sent', detail: 'The ticks update by themselves.' });
    } catch (error) {
      this.toast.add({ severity: 'error', summary: 'Not sent', detail: problemMessage(error) });
      if (this.canSend()) {
        this.messages.set(await this.api.messages().catch(() => this.messages()));
      }
    } finally {
      this.sending.set(false);
    }
  }

  tick(status: MessageStatus) {
    return messageTick(status);
  }

  /** Share of the 3-minute linking window that is left, for the countdown ring. */
  readonly timeLeftPercent = computed(() => Math.round((this.secondsLeft() / LINK_SECONDS) * 100));

  private async doUnlink(summary: string): Promise<void> {
    this.busy.set(true);
    try {
      this.onAccount(await this.api.unlink());
      this.toast.add({ severity: 'info', summary });
    } catch (error) {
      this.toast.add({ severity: 'error', summary: 'Could not unlink', detail: problemMessage(error) });
    } finally {
      this.busy.set(false);
    }
  }

  private onAccount(account: WhatsAppAccount): void {
    const before = this.account();
    this.account.set(account);
    // Connected again: messages paused while it was not linked wait for the user's decision.
    if (before && before.status !== 'connected' && account.status === 'connected') {
      void this.gate.offerPaused();
    }

    if (account.status !== 'linking') {
      this.method.set(null);
      this.qrImage.set(null);
      this.pairingCode.set(null);
      this.stopCountdown();
    }
  }

  private async showQr(qr: string): Promise<void> {
    if (this.method() !== 'qr') {
      return;
    }

    // Loaded only when a QR is shown, so the library is not part of the first download.
    const QRCode = await import('qrcode');
    this.qrImage.set(await QRCode.toDataURL(qr, { margin: 1, width: 264, errorCorrectionLevel: 'M' }));
  }

  private upsert(message: OutgoingMessage): void {
    this.messages.update((list) => {
      const others = list.filter((m) => m.id !== message.id);
      return [message, ...others].sort((a, b) => b.createdAt.localeCompare(a.createdAt)).slice(0, 20);
    });
  }

  private startCountdown(): void {
    this.stopCountdown();
    this.secondsLeft.set(LINK_SECONDS);
    this.countdown = setInterval(() => {
      this.secondsLeft.update((s) => Math.max(0, s - 1));
      if (this.secondsLeft() === 0) {
        this.stopCountdown();
      }
    }, 1000);
  }

  private stopCountdown(): void {
    if (this.countdown) {
      clearInterval(this.countdown);
      this.countdown = null;
    }
  }
}
