import { Routes } from '@angular/router';
import { guestGuard, roleGuard } from './core/session';

const claimant = roleGuard('Claimant');
const staff = roleGuard('ClaimsStaff');
const list = () => import('./claims/claim-list').then(module => module.ClaimList);
const detail = () => import('./claims/claim-detail').then(module => module.ClaimDetail);

export const routes: Routes = [
  { path: 'login', title: 'Sign in | Claims Workspace', canActivate: [guestGuard], loadComponent: () => import('./auth/login').then(module => module.Login) },
  { path: 'claims', title: 'My claims | Claims Workspace', canActivate: [claimant], loadComponent: list },
  { path: 'claims/new', title: 'New claim | Claims Workspace', canActivate: [claimant], loadComponent: () => import('./claims/new-claim').then(module => module.NewClaimPage) },
  { path: 'claims/:id', title: 'Claim details | Claims Workspace', canActivate: [claimant], loadComponent: detail },
  { path: 'staff', title: 'Staff queue | Claims Workspace', canActivate: [staff], loadComponent: list },
  { path: 'staff/:id', title: 'Claim review | Claims Workspace', canActivate: [staff], loadComponent: detail },
  { path: 'notifications', title: 'Notifications | Claims Workspace', canActivate: [claimant], loadComponent: () => import('./notifications/inbox').then(module => module.Inbox) },
  { path: '', pathMatch: 'full', redirectTo: 'login' },
  { path: '**', redirectTo: 'login' },
];
