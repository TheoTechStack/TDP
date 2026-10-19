<script setup lang="ts">
import { computed } from 'vue'
import type { DisputeStatus } from '@/api/types'
import { statusLabels } from '@/utils/format'

const props = defineProps<{ status: DisputeStatus }>()

const label = computed(() => statusLabels[props.status])
</script>

<template>
  <span class="badge" :class="`badge--${status}`" data-testid="status-badge">{{ label }}</span>
</template>

<style scoped>
/*
 * Three brand hues plus neutral. Fill vs outline carries meaning, not just colour:
 *   open      → cerulean   (outline = waiting, solid = being worked)
 *   decided   → navy solid = upheld, red outline = rejected
 *   withdrawn → neutral dashed
 */
.badge {
  display: inline-flex;
  align-items: center;
  height: 24px;
  padding: 0 9px;
  border-radius: 4px;
  border: 1px solid transparent;
  font-size: 0.7rem;
  font-weight: 700;
  letter-spacing: 0.07em;
  text-transform: uppercase;
  white-space: nowrap;
}

.badge--Submitted {
  color: var(--status-open);
  border-color: var(--status-open);
}

.badge--UnderReview {
  color: #fff;
  background: var(--status-open);
}

.badge--Upheld {
  color: #fff;
  background: var(--brand);
}

.badge--Rejected {
  color: var(--danger);
  border-color: var(--danger);
}

.badge--Withdrawn {
  color: var(--ink-muted);
  border: 1px dashed var(--line-strong);
}
</style>
