// Mirrors the API contracts in src/DisputePortal.Api/Features/*.

export type Role = 'Customer' | 'Agent'

/** A seeded demo login offered on the sign-in page. The API never sends its password. */
export interface DemoAccount {
  email: string
  displayName: string
  role: Role
  hint: string
}

export type DisputeStatus = 'Submitted' | 'UnderReview' | 'Upheld' | 'Rejected' | 'Withdrawn'

export type DisputeReason = 'Unauthorised' | 'Duplicate' | 'IncorrectAmount' | 'GoodsNotReceived' | 'Other'

export type DisputeOutcome = 'Upheld' | 'Rejected'

export type DisputeAction = 'withdraw' | 'startReview' | 'resolve'

export interface User {
  id: string
  email: string
  displayName: string
  role: Role
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: User
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface DisputeSummary {
  id: string
  status: DisputeStatus
  createdAt: string
}

export interface Transaction {
  id: string
  reference: string
  merchant: string
  category: string
  amount: number
  currency: string
  occurredAt: string
  latestDispute: DisputeSummary | null
  canDispute: boolean
  cannotDisputeReason: string | null
}

export interface TransactionBrief {
  id: string
  reference: string
  merchant: string
  amount: number
  currency: string
  occurredAt: string
}

export interface DisputeListItem {
  id: string
  status: DisputeStatus
  reason: DisputeReason
  createdAt: string
  updatedAt: string
  customerName: string
  transaction: TransactionBrief
}

export interface DisputeEvent {
  id: string
  fromStatus: DisputeStatus | null
  toStatus: DisputeStatus
  actorName: string
  actorRole: Role
  note: string | null
  occurredAt: string
}

export interface DisputeDetail {
  id: string
  status: DisputeStatus
  reason: DisputeReason
  description: string
  resolutionNote: string | null
  createdAt: string
  updatedAt: string
  customerName: string
  transaction: TransactionBrief
  history: DisputeEvent[]
  allowedActions: DisputeAction[]
}
