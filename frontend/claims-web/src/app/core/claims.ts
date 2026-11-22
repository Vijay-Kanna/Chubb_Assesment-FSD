export const claimStatuses = [
  'Submitted', 'UnderReview', 'AwaitingInformation', 'Approved', 'Rejected', 'Settled',
] as const;
export type ClaimStatus = typeof claimStatuses[number];
export const claimTypes = ['Motor', 'Property'] as const;
export const claimCurrencies = ['MYR', 'SGD', 'AUD', 'HKD', 'NZD', 'USD'] as const;
export type ClaimType = typeof claimTypes[number];
export type ClaimCurrency = typeof claimCurrencies[number];
export interface ClaimAmount {
  amount: number;
  currency: ClaimCurrency;
}

export function parseClaimAmount(value: string): number | null {
  const amount = Number(value);
  return /^\d+(\.\d{1,2})?$/.test(value.trim()) && amount <= 1_000_000_000 ? amount : null;
}

export const amountValidationMessage = 'Enter an amount from 0 to 1,000,000,000 with up to two decimal places.';
export type ReviewAction = 'pick-up' | 'questions' | 'replies' | 'approve' | 'reject' | 'settle' | 'rejection-reason';
export type Role = 'Claimant' | 'ClaimsStaff';

export const statusLabels: Record<ClaimStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  AwaitingInformation: 'Awaiting information',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Settled: 'Settled',
};

export const historyLabels: Record<string, string> = {
  Submitted: 'Claim submitted',
  Assigned: 'Assigned to claims staff',
  QuestionAsked: 'Question sent',
  ReplyReceived: 'Reply received',
  Approved: 'Claim approved',
  Rejected: 'Claim rejected',
  Settled: 'Settlement recorded',
  ExposureUpdated: 'Exposure estimate updated',
  RejectionReasonRecorded: 'Decision reason recorded',
};

export interface Claim {
  id: string;
  claimantId: string;
  assignedStaffId: string | null;
  type: ClaimType;
  incidentDate: string;
  description: string;
  status: ClaimStatus;
  createdAt: string;
  updatedAt: string;
  rejectionReason: string | null;
  requestedAmountMinor: number | null;
  requestedCurrency: ClaimCurrency | null;
  approvedAmountMinor: number | null;
  approvedCurrency: ClaimCurrency | null;
}

export interface NewClaim {
  type: ClaimType;
  incidentDate: string;
  description: string;
  requestedAmount?: number;
  requestedCurrency?: ClaimCurrency;
}

export interface ClaimMessage {
  id: number;
  senderUserId: string;
  body: string;
  sentAt: string;
}

export interface HistoryEntry {
  id: number;
  actorUserId: string;
  action: string;
  previousStatus: ClaimStatus | null;
  newStatus: ClaimStatus;
  occurredAt: string;
  claimMessageId: number | null;
}

export interface Notification {
  eventId: string;
  claimId: string;
  type: 'QuestionAsked' | 'Approved' | 'Rejected' | 'Settled';
  createdAt: string;
  message: string;
}

export const claimReference = (id: string) => 'CLM-' + id.slice(0, 8).toUpperCase();
export const claimsApi = '/api/claims';
export const notificationsApi = '/api/notifications';
export const claimUrl = (id: string) => claimsApi + '/claims/' + encodeURIComponent(id);
export const todayUtc = () => new Date().toISOString().slice(0, 10);
