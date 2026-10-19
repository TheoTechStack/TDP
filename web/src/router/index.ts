import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import type { Role } from '@/api/types'

declare module 'vue-router' {
  interface RouteMeta {
    public?: boolean
    role?: Role
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('@/views/LoginView.vue'), meta: { public: true } },
    {
      path: '/',
      name: 'home',
      redirect: () => (useAuthStore().isAgent ? { name: 'disputes' } : { name: 'transactions' }),
    },
    {
      path: '/transactions',
      name: 'transactions',
      component: () => import('@/views/TransactionsView.vue'),
      meta: { role: 'Customer' },
    },
    { path: '/disputes', name: 'disputes', component: () => import('@/views/DisputesView.vue') },
    {
      path: '/disputes/:id',
      name: 'dispute',
      component: () => import('@/views/DisputeDetailView.vue'),
      props: true,
    },
    { path: '/:pathMatch(.*)*', redirect: { name: 'home' } },
  ],
})

router.beforeEach((to) => {
  const auth = useAuthStore()
  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: to.fullPath !== '/' ? { redirect: to.fullPath } : {} }
  }
  if (to.name === 'login' && auth.isAuthenticated) {
    return { name: 'home' }
  }
  if (to.meta.role && auth.user?.role !== to.meta.role) {
    return { name: 'home' }
  }
  return true
})
