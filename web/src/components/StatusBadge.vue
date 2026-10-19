<script setup lang="ts">
import { computed } from 'vue'
import type { DisputeStatus } from '@/api/types'
import { statusLabels } from '@/utils/format'

const props = defineProps<{ status: DisputeStatus }>()

const label = computed(() => statusLabels[props.status])
</script>

<template>
  <span class="badge" :class="`badge--${status}`" data-testid="status-badge">
    <span class="dot" aria-hidden="true" />
    {{ label }}
  </span>
</template>

<style scoped>
.badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 3px 10px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
  white-space: nowrap;
}

.dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: currentColor;
}

.badge--Submitted {
  background: #e8effc;
  color: #2a5bb8;
}

.badge--UnderReview {
  background: #fdf3dc;
  color: #94620a;
}

.badge--Upheld {
  background: #e3f4ee;
  color: #12795c;
}

.badge--Rejected {
  background: #fbeaec;
  color: #b4232f;
}

.badge--Withdrawn {
  background: #eef0ef;
  color: #5b6b68;
}
</style>
