<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type { DisputeListItem, DisputeStatus, Paged } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { formatDate, formatMoney, reasonLabels, statusLabels } from '@/utils/format'
import StatusBadge from '@/components/StatusBadge.vue'
import PaginationBar from '@/components/PaginationBar.vue'

const auth = useAuthStore()
const router = useRouter()

const result = ref<Paged<DisputeListItem> | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const page = ref(1)
const status = ref<DisputeStatus | undefined>(undefined)

const filters: { label: string; value: DisputeStatus | undefined }[] = [
  { label: 'All', value: undefined },
  ...(Object.keys(statusLabels) as DisputeStatus[]).map((s) => ({ label: statusLabels[s], value: s })),
]

const title = computed(() => (auth.isAgent ? 'Dispute queue' : 'My disputes'))
const subtitle = computed(() =>
  auth.isAgent
    ? 'Every dispute raised by customers. Open one to review it and record a decision.'
    : 'Every dispute you have raised, including decided and withdrawn ones.',
)

async function load() {
  loading.value = true
  error.value = null
  try {
    result.value = await api.disputes({ page: page.value, pageSize: 15, status: status.value })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not load disputes.'
  } finally {
    loading.value = false
  }
}

function setFilter(value: DisputeStatus | undefined) {
  status.value = value
  page.value = 1
  load()
}

function changePage(next: number) {
  page.value = next
  load()
}

function open(id: string) {
  router.push({ name: 'dispute', params: { id } })
}

onMounted(load)
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <h1>{{ title }}</h1>
        <p>{{ subtitle }}</p>
      </div>
    </header>

    <div class="filters" role="tablist" aria-label="Filter by status">
      <button
        v-for="f in filters"
        :key="f.label"
        role="tab"
        class="chip"
        :class="{ active: status === f.value }"
        :aria-selected="status === f.value"
        @click="setFilter(f.value)"
      >
        {{ f.label }}
      </button>
    </div>

    <div v-if="error" class="alert alert-error" role="alert">{{ error }}</div>

    <section class="card">
      <table class="table">
        <thead>
          <tr>
            <th>Raised</th>
            <th>Transaction</th>
            <th v-if="auth.isAgent" class="hide-sm">Customer</th>
            <th class="hide-sm">Reason</th>
            <th class="right">Amount</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody v-if="loading && !result">
          <tr v-for="n in 5" :key="n">
            <td :colspan="auth.isAgent ? 6 : 5"><div class="skeleton" /></td>
          </tr>
        </tbody>
        <tbody v-else-if="result && result.items.length > 0">
          <tr
            v-for="d in result.items"
            :key="d.id"
            class="row-link dispute-row"
            tabindex="0"
            @click="open(d.id)"
            @keydown.enter="open(d.id)"
          >
            <td class="num nowrap c-date">{{ formatDate(d.createdAt) }}</td>
            <td class="c-main">
              <div class="merchant">{{ d.transaction.merchant }}</div>
              <div class="muted small num">{{ d.transaction.reference }}</div>
            </td>
            <td v-if="auth.isAgent" class="hide-sm">{{ d.customerName }}</td>
            <td class="hide-sm muted small">{{ reasonLabels[d.reason] }}</td>
            <td class="right num amount c-amount">{{ formatMoney(d.transaction.amount, d.transaction.currency) }}</td>
            <td class="c-status"><StatusBadge :status="d.status" /></td>
          </tr>
        </tbody>
        <tbody v-else>
          <tr>
            <td :colspan="auth.isAgent ? 6 : 5" class="empty">
              No disputes{{ status ? ` with status “${statusLabels[status]}”` : '' }} yet.
              <template v-if="!auth.isAgent && !status">
                <br />
                <RouterLink :to="{ name: 'transactions' }">Go to transactions</RouterLink> to raise one.
              </template>
            </td>
          </tr>
        </tbody>
      </table>
      <PaginationBar
        v-if="result"
        :page="result.page"
        :total-pages="result.totalPages"
        :total-count="result.totalCount"
        @change="changePage"
      />
    </section>
  </div>
</template>

<style scoped>
.filters {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}

.chip {
  border: 1px solid var(--line-strong);
  background: var(--surface);
  border-radius: 999px;
  padding: 6px 14px;
  font: inherit;
  font-size: 0.85rem;
  cursor: pointer;
  color: var(--ink-muted);
}

.chip.active {
  background: var(--brand);
  border-color: var(--brand);
  color: #fff;
}

.merchant {
  font-weight: 560;
}

.small {
  font-size: 0.8rem;
}

.amount {
  font-weight: 600;
}

.nowrap {
  white-space: nowrap;
}

@media (max-width: 640px) {
  .dispute-row {
    display: grid !important;
    grid-template-columns: 1fr auto;
    grid-template-areas:
      'main amount'
      'date status';
    gap: 4px 12px;
    padding: 14px 16px;
  }

  .c-main {
    grid-area: main;
  }

  .c-amount {
    grid-area: amount;
  }

  .c-date {
    grid-area: date;
    color: var(--ink-muted);
    font-size: 0.85rem;
  }

  .c-status {
    grid-area: status;
    text-align: right;
  }
}

.row-link:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: -2px;
}
</style>
