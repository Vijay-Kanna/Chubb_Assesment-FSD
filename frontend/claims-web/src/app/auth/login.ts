import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { email, form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideShieldCheck } from '@ng-icons/lucide';
import { Session } from '../core/session';

@Component({
  imports: [FormField, NgIcon],
  viewProviders: [provideIcons({ lucideArrowRight, lucideShieldCheck })],
  templateUrl: './login.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Login {
  private readonly session = inject(Session);
  private readonly credentials = signal({ email: '', password: '' });
  protected readonly error = signal('');
  protected readonly login = form(this.credentials, fields => {
    required(fields.email, { message: 'Enter your email address.' });
    email(fields.email, { message: 'Enter a valid email address.' });
    maxLength(fields.email, 254);
    required(fields.password, { message: 'Enter your password.' });
    maxLength(fields.password, 256);
  });

  protected selectAccount(email: string) {
    this.error.set('');
    this.credentials.update(current => ({ ...current, email }));
  }

  protected signIn(event: Event) {
    event.preventDefault();
    this.error.set('');
    void submit(this.login, async () => {
      try {
        const { email, password } = this.credentials();
        await this.session.signIn(email, password);
        this.credentials.update(current => ({ ...current, password: '' }));
      } catch (error) {
        this.error.set(error instanceof HttpErrorResponse && error.status === 401
          ? 'That email and password did not match.'
          : error instanceof HttpErrorResponse && error.status === 429
            ? 'Too many attempts. Wait a minute and try again.'
            : 'Sign-in is unavailable. Check that the Claims API is running.');
      }
    });
  }
}
