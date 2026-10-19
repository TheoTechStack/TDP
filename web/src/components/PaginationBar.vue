<script setup lang="ts">
defineProps<{ page: number; totalPages: number; totalCount: number }>()
const emit = defineEmits<{ change: [page: number] }>()
</script>

<template>
  <nav v-if="totalPages > 1" class="pager" aria-label="Pagination">
    <span class="muted">Page {{ page }} of {{ totalPages }} · {{ totalCount }} total</span>
    <div class="pager-buttons">
      <button class="btn btn-sm" :disabled="page <= 1" @click="emit('change', page - 1)">Previous</button>
      <button class="btn btn-sm" :disabled="page >= totalPages" @click="emit('change', page + 1)">Next</button>
    </div>
  </nav>
</template>

<style scoped>
.pager {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
  border-top: 1px solid var(--line);
  font-size: 0.85rem;
}

.pager-buttons {
  display: flex;
  gap: 8px;
}
</style>
