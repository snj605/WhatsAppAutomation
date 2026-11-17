import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { MessageModule } from 'primeng/message';
import { PasswordModule } from 'primeng/password';
import { AuthService } from '../../../core/auth/auth.service';
import { problemMessage } from '../../../core/http/problem';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, ButtonModule, IconFieldModule, InputIconModule, InputTextModule, PasswordModule, MessageModule],
  templateUrl: './login.page.html',
  styleUrl: './login.page.css',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  /** Where to go after signing in; filled from ?returnUrl=… by the router. */
  readonly returnUrl = input<string>();

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(NonNullableFormBuilder).group({
    userName: ['', [Validators.required, Validators.maxLength(256)]],
    password: ['', [Validators.required, Validators.maxLength(1024)]],
  });

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      const { userName, password } = this.form.getRawValue();
      await this.auth.signIn(userName.trim(), password);
      // Only same-site paths are accepted, so a crafted link cannot send you to another website.
      const target = this.returnUrl()?.startsWith('/') && !this.returnUrl()?.startsWith('//') ? this.returnUrl()! : '/';
      await this.router.navigateByUrl(target);
    } catch (e) {
      this.error.set(problemMessage(e, 'Sign-in failed. Please try again.'));
      this.form.controls.password.reset();
    } finally {
      this.busy.set(false);
    }
  }
}
