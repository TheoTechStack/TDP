<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { api, ApiError } from '@/api/client'
import type { DisputeDetail, Paged, Transaction } from '@/api/types'
import { formatDate, formatMoney } from '@/utils/format'
import StatusBadge from '@/components/StatusBadge.vue'
import PaginationBar from '@/components/PaginationBar.vue'
import RaiseDisputeDialog from '@/components/RaiseDisputeDialog.vue'

const router = useRouter()

const result = ref<Paged<Transaction> | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const page = ref(1)
const search = ref('')
const disputing = ref<Transaction | null>(null)
const notice = ref<string | null>(null)

let latestRequest = 0
async function load() {
  const requestId = ++latestRequest
  loading.value = true
  error.value = null
  try {
    const data = await api.transactions({ page: page.value, pageSize: 15, search: search.value.trim() })
    if (requestId === latestRequest) result.value = data
  } catch (e) {
    if (requestId === latestRequest) error.value = e instanceof ApiError ? e.message : 'Could not load transactions.'
  } finally {
    if (requestId === latestRequest) loading.value = false
  }
}

let debounce: ReturnType<typeof setTimeout> | undefined
watch(search, () => {
  clearTimeout(debounce)
  debounce = setTimeout(() => {
    page.value = 1
    load()
  }, 250)
})

function changePage(next: number) {
  page.value = next
  load()
}

function onRaised(dispute: DisputeDetail) {
  disputing.value = null
  notice.value = `Dispute submitted for ${dispute.transaction.merchant}. We'll keep you updated here.`
  load()
}

onMounted(load)
</script>

<template>
  <div class="page">
    <header class="page-header">
      <div>
        <h1>Transactions</h1>
        <p>Spot something wrong? You can dispute a transaction within 120 days.</p>
      </div>
      <input v-model="search" class="input search" type="search" placeholder="Search merchant or reference" />
    </header>

    <div v-if="notice" class="alert alert-info" role="status">
      {{ notice }}
      <a href="#" @click.prevent="router.push({ name: 'disputes' })">View my disputes</a>
    </div>
    <div v-if="error" class="alert alert-error" role="alert">{{ error }}</div>

    <section class="card">
      <table class="table">
        <thead>
          <tr>
            <th>Date</th>
            <th>Merchant</th>
            <th class="hide-sm">Reference</th>
            <th class="right">Amount</th>
            <th>Dispute</th>
            <th class="right"><span class="sr-only">Actions</span></th>
          </tr>
        </thead>
        <tbody v-if="loading && !result">
          <tr v-for="n in 6" :key="n">
            <td colspan="6"><div class="skeleton" /></td>
          </tr>
        </tbody>
        <tbody v-else-if="result && result.items.length > 0" :class="{ stale: loading }">
          <tr v-for="t in result.items" :key="t.id" class="txn">
            <td class="num nowrap c-date">{{ formatDate(t.occurredAt) }}</td>
            <td class="c-main">
              <div class="merchant">{{ t.merchant }}</div>
              <div class="muted small">{{ t.category }}</div>
            </td>
            <td class="hide-sm muted num small">{{ t.reference }}</td>
            <td class="right num amount c-amount">{{ formatMoney(t.amount, t.currency) }}</td>
            <td class="c-status">
              <RouterLink
                v-if="t.latestDispute"
                :to="{ name: 'dispute', params: { id: t.latestDispute.id } }"
                class="badge-link"
              >
                <StatusBadge :status="t.latestDispute.status" />
              </RouterLink>
              <span v-else class="muted">—</span>
            </td>
            <td class="right c-action">
              <button
                class="btn btn-sm"
                :disabled="!t.canDispute"
                :title="t.cannotDisputeReason ?? 'Dispute this transaction'"
                @click="disputing = t"
              >
                Dispute
              </button>
            </td>
          </tr>
        </tbody>
        <tbody v-else>
          <tr>
            <td colspan="6" class="empty">No transactions match “{{ search }}”.</td>
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

    <RaiseDisputeDialog v-if="disputing" :transaction="disputing" @close="disputing = null" @raised="onRaised" />
  </div>
</template>

<style scoped>
.search {
  width: min(320px, 100%);
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

.stale {
  opacity: 0.55;
  transition: opacity 0.15s;
}

.badge-link {
  text-decoration: none;
}

@media (max-width: 640px) {
  .txn {
    display: grid !important;
    grid-template-columns: 1fr auto;
    grid-template-areas:
      'main amount'
      'date status'
      'action action';
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

  .c-status .muted {
    display: none;
  }

  .c-action {
    grid-area: action;
    margin-top: 8px;
  }

  .c-action .btn {
    width: 100%;
  }

  .search {
    width: 100%;
  }
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
}
</style>
