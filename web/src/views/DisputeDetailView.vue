<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, ApiError } from '@/api/client'
import type { DisputeDetail, DisputeOutcome } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { formatDate, formatDateTime, formatMoney, reasonLabels, statusLabels } from '@/utils/format'
import StatusBadge from '@/components/StatusBadge.vue'
import ModalDialog from '@/components/ModalDialog.vue'

const props = defineProps<{ id: string }>()
const auth = useAuthStore()

const dispute = ref<DisputeDetail | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const busy = ref(false)
const actionError = ref<string | null>(null)

const resolving = ref(false)
const outcome = ref<DisputeOutcome | ''>('')
const note = ref('')

const can = computed(() => new Set(dispute.value?.allowedActions ?? []))
const newestFirst = computed(() => [...(dispute.value?.history ?? [])].reverse())

async function load() {
  loading.value = true
  error.value = null
  try {
    dispute.value = await api.dispute(props.id)
  } catch (e) {
    error.value = e instanceof ApiError && e.status === 404 ? 'Dispute not found.' : 'Could not load this dispute.'
  } finally {
    loading.value = false
  }
}

async function run(action: () => Promise<DisputeDetail>) {
  busy.value = true
  actionError.value = null
  try {
    dispute.value = await action()
    resolving.value = false
  } catch (e) {
    actionError.value = e instanceof ApiError ? e.message : 'The action failed. Please try again.'
    // A 409 means the dispute moved on (e.g. another agent acted); show the latest state.
    if (e instanceof ApiError && e.status === 409) await load()
  } finally {
    busy.value = false
  }
}

function startReview() {
  run(() => api.startReview(props.id))
}

function withdraw() {
  if (confirm('Withdraw this dispute? You can raise it again later if needed.')) {
    run(() => api.withdraw(props.id))
  }
}

function submitResolution() {
  if (!outcome.value || !note.value.trim()) return
  const chosen = outcome.value
  run(() => api.resolve(props.id, chosen, note.value.trim()))
}

function describe(from: string | null, to: keyof typeof statusLabels): string {
  if (!from) return 'Dispute submitted'
  if (to === 'UnderReview') return 'Review started'
  if (to === 'Withdrawn') return 'Withdrawn by customer'
  return `Decision: ${statusLabels[to]}`
}

watch(() => props.id, load, { immediate: true })
</script>

<template>
  <div class="page narrow">
    <RouterLink :to="{ name: 'disputes' }" class="back">← {{ auth.isAgent ? 'Dispute queue' : 'My disputes' }}</RouterLink>

    <div v-if="loading && !dispute" class="card pad"><div class="skeleton" /></div>
    <div v-else-if="error" class="alert alert-error" role="alert">{{ error }}</div>

    <template v-else-if="dispute">
      <header class="page-header">
        <div>
          <div class="eyebrow">Dispute · {{ formatDate(dispute.createdAt) }}</div>
          <h1>{{ dispute.transaction.merchant }}</h1>
        </div>
        <StatusBadge :status="dispute.status" class="big" />
      </header>

      <div v-if="actionError" class="alert alert-error" role="alert">{{ actionError }}</div>

      <div class="grid">
        <section class="card pad">
          <h2>Details</h2>
          <dl class="facts">
            <dt>Amount</dt>
            <dd class="num strong">{{ formatMoney(dispute.transaction.amount, dispute.transaction.currency) }}</dd>
            <dt>Transaction date</dt>
            <dd>{{ formatDate(dispute.transaction.occurredAt) }}</dd>
            <dt>Reference</dt>
            <dd class="num">{{ dispute.transaction.reference }}</dd>
            <template v-if="auth.isAgent">
              <dt>Customer</dt>
              <dd>{{ dispute.customerName }}</dd>
            </template>
            <dt>Reason</dt>
            <dd>{{ reasonLabels[dispute.reason] }}</dd>
            <dt>Customer's description</dt>
            <dd>{{ dispute.description || '—' }}</dd>
          </dl>

          <div v-if="dispute.resolutionNote" class="resolution" :class="dispute.status">
            <strong>{{ dispute.status === 'Upheld' ? 'Dispute upheld' : 'Dispute rejected' }}</strong>
            <p>{{ dispute.resolutionNote }}</p>
          </div>

          <div v-if="can.size > 0" class="actions">
            <button v-if="can.has('withdraw')" class="btn btn-danger" :disabled="busy" @click="withdraw">
              Withdraw dispute
            </button>
            <button v-if="can.has('startReview')" class="btn btn-primary" :disabled="busy" @click="startReview">
              Start review
            </button>
            <button v-if="can.has('resolve')" class="btn btn-primary" :disabled="busy" @click="resolving = true">
              Record decision
            </button>
          </div>
        </section>

        <section class="card pad">
          <h2>History</h2>
          <ol class="timeline">
            <li v-for="event in newestFirst" :key="event.id" :class="`t-${event.toStatus}`">
              <div class="t-head">
                <strong>{{ describe(event.fromStatus, event.toStatus) }}</strong>
                <time class="muted small num" :datetime="event.occurredAt">{{ formatDateTime(event.occurredAt) }}</time>
              </div>
              <div class="muted small">by {{ event.actorName }} ({{ event.actorRole.toLowerCase() }})</div>
              <p v-if="event.note" class="t-note">{{ event.note }}</p>
            </li>
          </ol>
        </section>
      </div>
    </template>

    <ModalDialog v-if="resolving" title="Record decision" @close="resolving = false">
      <form @submit.prevent="submitResolution">
        <fieldset class="field outcomes">
          <legend>Outcome</legend>
          <label :class="{ selected: outcome === 'Upheld' }">
            <input v-model="outcome" type="radio" value="Upheld" /> Uphold — customer is refunded
          </label>
          <label :class="{ selected: outcome === 'Rejected' }">
            <input v-model="outcome" type="radio" value="Rejected" /> Reject — transaction stands
          </label>
        </fieldset>
        <div class="field">
          <label for="note">Note to customer</label>
          <textarea id="note" v-model="note" maxlength="1000" placeholder="Explain the decision in plain language." />
        </div>
        <div class="modal-actions">
          <button type="button" class="btn" @click="resolving = false">Cancel</button>
          <button type="submit" class="btn btn-primary" :disabled="busy || !outcome || !note.trim()">Save decision</button>
        </div>
      </form>
    </ModalDialog>
  </div>
</template>

<style scoped>
.narrow {
  max-width: 980px;
}

.back {
  display: inline-block;
  margin-bottom: 16px;
  text-decoration: none;
  font-size: 0.9rem;
}

.eyebrow {
  font-size: 0.78rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--ink-faint);
  margin-bottom: 6px;
}

.big {
  font-size: 0.9rem;
  padding: 6px 14px;
}

.grid {
  display: grid;
  grid-template-columns: 1.1fr 0.9fr;
  gap: 16px;
  align-items: start;
}

@media (max-width: 820px) {
  .grid {
    grid-template-columns: 1fr;
  }
}

.pad {
  padding: 24px;
}

.facts {
  display: grid;
  grid-template-columns: 160px 1fr;
  gap: 10px 16px;
  margin: 16px 0 0;
  font-size: 0.93rem;
}

@media (max-width: 640px) {
  .facts {
    grid-template-columns: 1fr;
    gap: 2px;
  }

  .facts dd {
    margin-bottom: 10px;
  }

  .pad {
    padding: 18px;
  }
}

.facts dt {
  color: var(--ink-muted);
}

.facts dd {
  margin: 0;
  white-space: pre-wrap;
}

.strong {
  font-weight: 650;
}

.resolution {
  margin-top: 20px;
  padding: 14px 16px;
  border-radius: var(--radius-sm);
  font-size: 0.92rem;
}

.resolution p {
  margin: 4px 0 0;
}

.resolution.Upheld {
  background: #e3f4ee;
  color: #0e5c46;
}

.resolution.Rejected {
  background: var(--danger-soft);
  color: var(--danger);
}

.actions {
  display: flex;
  gap: 8px;
  margin-top: 24px;
  padding-top: 20px;
  border-top: 1px solid var(--line);
}

.timeline {
  list-style: none;
  margin: 16px 0 0;
  padding: 0;
  position: relative;
}

.timeline li {
  position: relative;
  padding: 0 0 20px 22px;
  border-left: 2px solid var(--line);
  margin-left: 5px;
}

.timeline li:last-child {
  border-left-color: transparent;
  padding-bottom: 0;
}

.timeline li::before {
  content: '';
  position: absolute;
  left: -7px;
  top: 3px;
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: var(--surface);
  border: 2px solid var(--ink-faint);
}

.t-Submitted::before {
  border-color: #2a5bb8 !important;
}

.t-UnderReview::before {
  border-color: #94620a !important;
}

.t-Upheld::before {
  border-color: #12795c !important;
  background: #12795c !important;
}

.t-Rejected::before {
  border-color: #b4232f !important;
  background: #b4232f !important;
}

.t-head {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
  font-size: 0.92rem;
}

.t-note {
  margin: 6px 0 0;
  font-size: 0.88rem;
  padding: 8px 10px;
  background: var(--canvas);
  border-radius: 6px;
}

.small {
  font-size: 0.8rem;
}

.outcomes {
  border: none;
  padding: 0;
  margin: 0 0 16px;
}

.outcomes label {
  display: flex;
  gap: 10px;
  align-items: center;
  padding: 10px 12px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-sm);
  margin-top: 8px;
  font-weight: 450;
  cursor: pointer;
}

.outcomes label.selected {
  border-color: var(--accent);
  background: var(--accent-soft);
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
</style>
