import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBell, lucideChartNoAxesColumnIncreasing, lucideFiles, lucideLogOut, lucideShieldCheck } from '@ng-icons/lucide';
import { Session } from './core/session';
import { LiveUpdates } from './core/live-updates';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgIcon],
  viewProviders: [provideIcons({ lucideBell, lucideChartNoAxesColumnIncreasing, lucideFiles, lucideLogOut, lucideShieldCheck })],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly session = inject(Session);
  protected readonly live = inject(LiveUpdates);
  protected readonly initials = computed(() => this.session.current()?.email.slice(0, 2).toUpperCase());
}
