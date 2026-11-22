import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell, lucideFiles, lucideLogOut, lucideShieldCheck } from '@ng-icons/lucide';
import { Session } from './core/session';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgIcon],
  viewProviders: [provideIcons({ lucideBell, lucideFiles, lucideLogOut, lucideShieldCheck })],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly session = inject(Session);
  protected readonly initials = computed(() => this.session.current()?.email.slice(0, 2).toUpperCase());
}
