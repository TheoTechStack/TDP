import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, configureClient } from '@/api/client'
import type { LoginResponse, User } from '@/api/types'

const STORAGE_KEY = 'dispute-portal.session'

interface Session {
  accessToken: string
  expiresAt: string
  user: User
}

function loadSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const session = JSON.parse(raw) as Session
    return new Date(session.expiresAt) > new Date() ? session : null
  } catch {
    return null
  }
}

/**
 * Session lives in sessionStorage (cleared when the tab closes) rather than localStorage, and the
 * token is short-lived. The API is same-origin and sends a strict CSP, which limits XSS exposure.
 */
export const useAuthStore = defineStore('auth', () => {
  const session = ref<Session | null>(loadSession())

  const user = computed(() => session.value?.user ?? null)
  const isAuthenticated = computed(() => session.value !== null)
  const isAgent = computed(() => user.value?.role === 'Agent')

  function setSession(response: LoginResponse | null) {
    session.value = response
    if (response) sessionStorage.setItem(STORAGE_KEY, JSON.stringify(response))
    else sessionStorage.removeItem(STORAGE_KEY)
  }

  async function login(email: string, password: string) {
    setSession(await api.login(email, password))
  }

  function logout() {
    setSession(null)
  }

  configureClient({
    getToken: () => session.value?.accessToken ?? null,
    onUnauthorized: () => {
      logout()
      window.location.assign('/login?expired=1')
    },
  })

  return { user, isAuthenticated, isAgent, login, logout }
})
