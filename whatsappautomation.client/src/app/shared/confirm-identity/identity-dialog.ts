import { Component, effect, inject, signal } from '@angular/core';
import { DialogModule } from 'primeng/dialog';
import { IdentityCheck } from './identity-check';

/** The "Confirm it's you" dialog, placed once in the app root. */
@Component({
  selector: 'app-identity-dialog',
  imports: [DialogModule],
  template: `
    <p-dialog header="Confirm it's you" [modal]="true" [visible]="check.reason() !== null" (visibleChange)="!$event && check.cancel()"
      [style]="{ width: 'min(92vw, 400px)' }" [draggable]="false">
      <form class="ap-form" (submit)="$event.preventDefault(); check.check(password())">
        <p class="reason"><i class="pi pi-shield" aria-hidden="true"></i> {{ check.reason() }} Type your password.</p>
        <label for="identity-password">Password</label>
        <input id="identity-password" type="password" autocomplete="current-password" [value]="password()"
          (input)="password.set($any($event.target).value)" [disabled]="check.busy()" autofocus />
        @if (check.error(); as e) {<p class="error" role="alert">{{ e }}</p>}
        <div class="actions">
          <button type="button" class="ap-btn" (click)="check.cancel()" [disabled]="check.busy()">Cancel</button>
          <button type="submit" class="ap-btn primary" [disabled]="check.busy()">Confirm</button>
        </div>
      </form>
    </p-dialog>
  `,
  styles: `
    .reason { margin: 0 0 8px; line-height: 1.4; }
    .error { color: var(--p-red-600, #d92d20); margin: 4px 0 0; }
    .actions { display: flex; justify-content: flex-end; gap: 8px; margin-top: 12px; }
  `,
})
export class IdentityDialog {
  protected readonly check = inject(IdentityCheck);
  protected readonly password = signal('');

  constructor() {
    // A fresh, empty field every time the dialog opens or closes.
    effect(() => {
      this.check.reason();
      this.password.set('');
    });
  }
}
