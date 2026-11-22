import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Claim, ClaimAmount, NewClaim, ReviewAction, claimUrl, claimsApi } from './claims';

@Injectable({ providedIn: 'root' })
export class ClaimsClient {
  private readonly http = inject(HttpClient);

  submit(claim: NewClaim) {
    return firstValueFrom(this.http.post<Claim>(claimsApi + '/claims', claim));
  }

  review(id: string, action: ReviewAction, body?: string | ClaimAmount) {
    const url = claimUrl(id) + '/' + action;
    if (action === 'rejection-reason') {
      return firstValueFrom(this.http.put<Claim>(url, { reason: body }));
    }
    const payload = action === 'reject' ? { reason: body } : typeof body === 'object' ? body : body ? { body } : null;
    return firstValueFrom(this.http.post<Claim>(url, payload));
  }

  saveExposure(id: string, amount: number, currency: string) {
    return firstValueFrom(this.http.put<Claim>(claimUrl(id) + '/exposure', { amount, currency }));
  }
}

export function errorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;
  if (error.status === 404) return 'This claim is unavailable or you do not have access.';
  if (error.status === 403) return 'Your account cannot perform this action.';
  if (error.status === 409) return 'The claim changed. Refresh and try again.';
  if (error.status === 0 || error.status >= 500) return 'The service is unavailable. Try again in a moment.';
  return fallback;
}
