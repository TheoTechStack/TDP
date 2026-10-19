<script setup lang="ts">
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const showShell = computed(() => auth.isAuthenticated && !route.meta.public)

async function signOut() {
  auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <header v-if="showShell" class="topbar">
    <div class="topbar-inner">
      <RouterLink to="/" class="logo">
        <img src="/favicon.svg" alt="" width="26" height="26" />
        <span>Disputes</span>
      </RouterLink>

      <nav class="nav">
        <RouterLink v-if="!auth.isAgent" :to="{ name: 'transactions' }">Transactions</RouterLink>
        <RouterLink :to="{ name: 'disputes' }">{{ auth.isAgent ? 'Dispute queue' : 'My disputes' }}</RouterLink>
      </nav>

      <div class="user">
        <span class="who">
          {{ auth.user?.displayName }}
          <span v-if="auth.isAgent" class="role">Agent</span>
        </span>
        <button class="btn btn-sm" @click="signOut">Sign out</button>
      </div>
    </div>
  </header>

  <RouterView />
</template>

<style scoped>
.topbar {
  position: sticky;
  top: 0;
  z-index: 10;
  background: var(--brand);
  color: #fff;
}

.topbar-inner {
  max-width: 1080px;
  margin: 0 auto;
  padding: 0 20px;
  height: 58px;
  display: flex;
  align-items: center;
  gap: 28px;
}

.logo {
  display: flex;
  align-items: center;
  gap: 10px;
  color: #fff;
  text-decoration: none;
  font-weight: 700;
  letter-spacing: -0.01em;
}

.nav {
  display: flex;
  gap: 4px;
  flex: 1;
}

.nav a {
  color: rgba(255, 255, 255, 0.72);
  text-decoration: none;
  font-size: 0.92rem;
  font-weight: 550;
  padding: 7px 12px;
  border-radius: 8px;
}

.nav a:hover {
  color: #fff;
}

.nav a.router-link-active {
  color: #fff;
  background: rgba(255, 255, 255, 0.12);
}

.user {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 0.88rem;
}

.who {
  display: flex;
  align-items: center;
  gap: 8px;
}

.role {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  background: #7ee0c3;
  color: var(--brand-strong);
  padding: 2px 8px;
  border-radius: 999px;
}

.user .btn {
  background: transparent;
  color: #fff;
  border-color: rgba(255, 255, 255, 0.3);
}

.nav a {
  white-space: nowrap;
}

@media (max-width: 640px) {
  .who,
  .logo span {
    display: none;
  }

  .topbar-inner {
    gap: 8px;
    padding: 0 12px;
  }

  .nav a {
    padding: 7px 8px;
    font-size: 0.85rem;
  }
}
</style>
