import { describe, expect, it } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from './StatusBadge.vue'

describe('StatusBadge', () => {
  it('renders a human label and a status-specific class', () => {
    const wrapper = mount(StatusBadge, { props: { status: 'UnderReview' } })

    expect(wrapper.text()).toBe('Under review')
    expect(wrapper.classes()).toContain('badge--UnderReview')
  })
})
