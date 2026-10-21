import { describe, expect, it } from 'vitest'
import { formatMoney, isActive, reasonLabels, statusLabels } from './format'

describe('formatMoney', () => {
  it('formats rand amounts with two decimals', () => {
    const text = formatMoney(1234.5, 'ZAR')
    expect(text).toMatch(/R/)
    expect(text.replace(/\s/g, '')).toMatch(/1.?234,50|1.?234\.50/)
  })
})

describe('labels', () => {
  it('has a label for every status and reason the API can return', () => {
    expect(Object.keys(statusLabels)).toEqual(['Submitted', 'UnderReview', 'Upheld', 'Rejected', 'Withdrawn'])
    expect(Object.keys(reasonLabels)).toEqual([
      'Unauthorised',
      'Duplicate',
      'IncorrectAmount',
      'GoodsNotReceived',
      'SubscriptionCancelled',
      'Other',
    ])
  })

  it('treats only submitted and under-review disputes as active', () => {
    expect(isActive('Submitted')).toBe(true)
    expect(isActive('UnderReview')).toBe(true)
    expect(isActive('Upheld')).toBe(false)
    expect(isActive('Withdrawn')).toBe(false)
  })
})
