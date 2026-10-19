<script setup lang="ts">
import { computed, ref } from 'vue'
import ModalDialog from './ModalDialog.vue'
import { api, ApiError } from '@/api/client'
import type { DisputeDetail, DisputeReason, Transaction } from '@/api/types'
import { formatDate, formatMoney, reasonLabels } from '@/utils/format'

const MAX_LENGTH = 1000

const props = defineProps<{ transaction: Transaction }>()
const emit = defineEmits<{ close: []; raised: [dispute: DisputeDetail] }>()

const reason = ref<DisputeReason | ''>('')
const description = ref('')
const submitting = ref(false)
const error = ref<string | null>(null)

const reasons = Object.entries(reasonLabels) as [DisputeReason, string][]

/** Mirrors the server-side rules for instant feedback; the API remains the source of truth. */
const validationMessage = computed(() => {
  if (!reason.value) return 'Choose a reason.'
  if (reason.value === 'Other' && description.value.trim().length === 0) return 'Tell us what went wrong.'
  if (description.value.length > MAX_LENGTH) return `Keep it under ${MAX_LENGTH} characters.`
  return null
})

async function submit() {
  if (validationMessage.value || !reason.value) return
  submitting.value = true
  error.value = null
  try {
    const dispute = await api.raiseDispute(props.transaction.id, reason.value, description.value.trim())
    emit('raised', dispute)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not submit the dispute. Please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <ModalDialog title="Dispute transaction" @close="emit('close')">
    <div class="txn">
      <div>
        <div class="merchant">{{ transaction.merchant }}</div>
        <div class="muted small">{{ formatDate(transaction.occurredAt) }} · {{ transaction.reference }}</div>
      </div>
      <div class="num amount">{{ formatMoney(transaction.amount, transaction.currency) }}</div>
    </div>

    <form novalidate @submit.prevent="submit">
      <div v-if="error" class="alert alert-error" role="alert">{{ error }}</div>

      <fieldset class="field reasons">
        <legend>What's wrong with this transaction?</legend>
        <label v-for="[value, label] in reasons" :key="value" class="reason" :class="{ selected: reason === value }">
          <input v-model="reason" type="radio" name="reason" :value="value" />
          {{ label }}
        </label>
      </fieldset>

      <div class="field">
        <label for="description">
          Details <span class="muted">{{ reason === 'Other' ? '(required)' : '(optional)' }}</span>
        </label>
        <textarea
          id="description"
          v-model="description"
          :maxlength="MAX_LENGTH"
          placeholder="Anything that helps us investigate: order numbers, dates, what you expected."
        />
        <span class="hint">{{ description.length }}/{{ MAX_LENGTH }}</span>
      </div>

      <div class="actions">
        <button type="button" class="btn" @click="emit('close')">Cancel</button>
        <button type="submit" class="btn btn-primary" :disabled="!!validationMessage || submitting">
          {{ submitting ? 'Submitting…' : 'Submit dispute' }}
        </button>
      </div>
    </form>
  </ModalDialog>
</template>

<style scoped>
.txn {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 16px;
  margin-bottom: 20px;
  border-radius: var(--radius-sm);
  background: var(--canvas);
}

.merchant {
  font-weight: 600;
}

.small {
  font-size: 0.82rem;
}

.amount {
  font-weight: 650;
  font-size: 1.05rem;
}

.reasons {
  border: none;
  padding: 0;
  margin: 0 0 16px;
  gap: 8px;
}

.reasons legend {
  margin-bottom: 8px;
}

.reason {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-sm);
  cursor: pointer;
  font-weight: 450 !important;
  font-size: 0.92rem !important;
}

.reason.selected {
  border-color: var(--accent);
  background: var(--accent-soft);
}

.reason input {
  accent-color: var(--accent);
}

.actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
