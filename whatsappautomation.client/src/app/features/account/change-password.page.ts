import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';

/**
 * Change your own password. Also the first screen after an admin chose your password: nothing else opens until
 * you choose your own.
 */
@Component({
  selector: 'app-change-password-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './change-password.page.html',
  styleUrl: '../../shared/approved.css',
})
export class ChangePasswordPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected current = '';
  protected next = '';
  protected repeat = '';
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly forced = computed(() => this.auth.mustChangePassword());
  /** Admins need 12 characters, members 6 (the server checks this too, and refuses obvious ones). */
  protected readonly minLength = computed(() => (this.auth.user()?.kind === 'member' ? 6 : 12));

  protected async save(): Promise<void> {
    this.error.set(null);
    if (!this.current || this.next.length < this.minLength()) {
      this.error.set(`The new password needs at least ${this.minLength()} characters.`);
      return;
    }

    if (this.next !== this.repeat) {
      this.error.set('The two new passwords are not the same.');
      return;
    }

    this.saving.set(true);
    try {
      await this.auth.changePassword(this.current, this.next);
      await this.router.navigateByUrl('/');
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected signOut(): void {
    void this.auth.signOut();
  }
}
