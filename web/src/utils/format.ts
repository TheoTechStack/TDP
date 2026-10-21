import type { DisputeReason, DisputeStatus } from '@/api/types'

const moneyFormatters = new Map<string, Intl.NumberFormat>()

export function formatMoney(amount: number, currency: string): string {
  let formatter = moneyFormatters.get(currency)
  if (!formatter) {
    formatter = new Intl.NumberFormat('en-ZA', { style: 'currency', currency })
    moneyFormatters.set(currency, formatter)
  }
  return formatter.format(amount)
}

const dateFormatter = new Intl.DateTimeFormat('en-ZA', { day: 'numeric', month: 'short', year: 'numeric' })
const dateTimeFormatter = new Intl.DateTimeFormat('en-ZA', {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
})

export const formatDate = (iso: string) => dateFormatter.format(new Date(iso))
export const formatDateTime = (iso: string) => dateTimeFormatter.format(new Date(iso))

export const statusLabels: Record<DisputeStatus, string> = {
  Submitted: 'Submitted',
  UnderReview: 'Under review',
  Upheld: 'Upheld',
  Rejected: 'Rejected',
  Withdrawn: 'Withdrawn',
}

export const reasonLabels: Record<DisputeReason, string> = {
  Unauthorised: "I didn't make or approve this (possible fraud)",
  Duplicate: 'I was charged more than once',
  IncorrectAmount: 'The amount is wrong',
  GoodsNotReceived: "I didn't receive the goods or service",
  SubscriptionCancelled: 'I cancelled this subscription or contract',
  Other: 'Something else',
}

export const isActive = (status: DisputeStatus) => status === 'Submitted' || status === 'UnderReview'
