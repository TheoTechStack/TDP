<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { api, ApiError } from '@/api/client'
import type { DemoAccount } from '@/api/types'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const submitting = ref(false)
const error = ref<string | null>(null)
const expired = route.query.expired === '1'

const passwordInput = ref<HTMLInputElement | null>(null)

// Offered only where the API seeds demo data; elsewhere the endpoint is 404 and the panel stays hidden.
// The password is never sent to the browser: it is shared with reviewers separately.
const demoAccounts = ref<DemoAccount[]>([])

onMounted(async () => {
  try {
    demoAccounts.value = await api.demoAccounts()
  } catch {
    demoAccounts.value = []
  }
})

function useDemo(account: DemoAccount) {
  email.value = account.email
  password.value = ''
  passwordInput.value?.focus()
}

const roleLabel = (role: DemoAccount['role']) => (role === 'Agent' ? 'Disputes agent' : 'Customer')

async function submit() {
  submitting.value = true
  error.value = null
  try {
    await auth.login(email.value, password.value)
    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : '/'
    await router.replace(redirect)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Could not sign in. Please try again.'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="login">
    <section class="panel card">
      <div class="brand">
        <img src="/favicon.svg" alt="" width="36" height="36" />
        <div>
          <h1>Sign in</h1>
          <p class="muted">View your transactions and manage disputes.</p>
        </div>
      </div>

      <div v-if="expired" class="alert alert-info">Your session expired. Please sign in again.</div>
      <div v-if="error" class="alert alert-error" role="alert">{{ error }}</div>

      <form @submit.prevent="submit">
        <div class="field">
          <label for="email">Email</label>
          <input id="email" v-model="email" class="input" type="email" autocomplete="username" required />
        </div>
        <div class="field">
          <label for="password">Password</label>
          <input
            id="password"
            ref="passwordInput"
            v-model="password"
            class="input"
            type="password"
            autocomplete="current-password"
            required
          />
        </div>
        <button class="btn btn-primary full" type="submit" :disabled="submitting">
          {{ submitting ? 'Signing in…' : 'Sign in' }}
        </button>
      </form>

      <div v-if="demoAccounts.length" class="demo">
        <p class="demo-title">Demo accounts <span class="muted">· pick one, then enter the demo password</span></p>
        <button
          v-for="account in demoAccounts"
          :key="account.email"
          type="button"
          class="demo-account"
          @click="useDemo(account)"
        >
          <span>
            <strong>{{ account.displayName }}</strong>
            <span class="muted"> · {{ roleLabel(account.role) }}</span>
          </span>
          <span class="muted small">{{ account.hint }}</span>
        </button>
      </div>
    </section>
  </main>
</template>

<style scoped>
.login {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background:
    radial-gradient(1200px 500px at 10% -10%, #dcecf6 0%, transparent 60%),
    radial-gradient(900px 400px at 110% 110%, #fbe5e4 0%, transparent 60%),
    var(--canvas);
}

.panel {
  width: min(420px, 100%);
  padding: 32px;
}

.brand {
  display: flex;
  gap: 14px;
  align-items: center;
  margin-bottom: 24px;
}

.brand p {
  margin: 2px 0 0;
  font-size: 0.9rem;
}

.full {
  width: 100%;
  height: 42px;
}

.demo {
  margin-top: 28px;
  padding-top: 20px;
  border-top: 1px solid var(--line);
  display: grid;
  gap: 8px;
}

.demo-title {
  margin: 0 0 4px;
  font-size: 0.82rem;
  font-weight: 600;
}

.demo-account {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 10px 12px;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--surface);
  font: inherit;
  font-size: 0.88rem;
  text-align: left;
  cursor: pointer;
}

.demo-account:hover {
  border-color: var(--accent);
  background: var(--accent-soft);
}

.small {
  font-size: 0.78rem;
}
</style>
