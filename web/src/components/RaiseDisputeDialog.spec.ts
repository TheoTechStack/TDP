import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import RaiseDisputeDialog from './RaiseDisputeDialog.vue'
import type { Transaction } from '@/api/types'

const transaction: Transaction = {
  id: 'txn-1',
  reference: 'TXN-1001-123456',
  merchant: 'Takealot.com',
  category: 'Online shopping',
  amount: 499.99,
  currency: 'ZAR',
  occurredAt: '2026-10-10T10:00:00Z',
  latestDispute: null,
  canDispute: true,
  cannotDisputeReason: null,
}

beforeAll(() => {
  // jsdom does not implement <dialog>.showModal()
  HTMLDialogElement.prototype.showModal = vi.fn()
})

afterEach(() => vi.unstubAllGlobals())

function submitButton(wrapper: ReturnType<typeof mount>) {
  return wrapper.get('button[type="submit"]')
}

describe('RaiseDisputeDialog', () => {
  it('requires a reason, and a description when the reason is Other', async () => {
    const wrapper = mount(RaiseDisputeDialog, { props: { transaction } })
    expect(submitButton(wrapper).attributes('disabled')).toBeDefined()

    await wrapper.get('input[value="Other"]').setValue()
    expect(submitButton(wrapper).attributes('disabled')).toBeDefined()

    await wrapper.get('textarea').setValue('The merchant refunded me but the charge is still here.')
    expect(submitButton(wrapper).attributes('disabled')).toBeUndefined()
  })

  it('posts the dispute and emits the created dispute', async () => {
    const created = { id: 'd-1', transaction: { merchant: 'Takealot.com' } }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(created), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(RaiseDisputeDialog, { props: { transaction } })
    await wrapper.get('input[value="Duplicate"]').setValue()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/transactions/txn-1/disputes', expect.objectContaining({ method: 'POST' }))
    expect(JSON.parse(fetchMock.mock.calls[0]![1].body)).toEqual({ reason: 'Duplicate', description: '' })
    expect(wrapper.emitted('raised')?.[0]).toEqual([created])
  })

  it('shows the API problem message when the server rejects the dispute', async () => {
    const problem = { title: 'This transaction already has a dispute.', code: 'already_disputed', status: 409 }
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(problem), { status: 409 })))

    const wrapper = mount(RaiseDisputeDialog, { props: { transaction } })
    await wrapper.get('input[value="Duplicate"]').setValue()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('already has a dispute')
    expect(wrapper.emitted('raised')).toBeUndefined()
  })
})
