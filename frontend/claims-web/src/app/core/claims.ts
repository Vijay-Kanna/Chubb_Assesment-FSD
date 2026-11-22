export const claimStatuses = [
  'Submitted', 'UnderReview', 'AwaitingInformation', 'Approved', 'Rejected', 'Settled',
] as const;
export type ClaimStatus = typeof claimStatuses[number];
export type ClaimType = 'Motor' | 'Property';
export type ReviewAction = 'pick-up' | 'questions' | 'replies' | 'approve' | 'reject' | 'settle';
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
}

export interface NewClaim {
  type: ClaimType;
  incidentDate: string;
  description: string;
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
