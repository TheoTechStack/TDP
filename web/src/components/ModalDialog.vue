<script setup lang="ts">
import { onMounted, ref } from 'vue'

defineProps<{ title: string }>()
const emit = defineEmits<{ close: [] }>()

const dialog = ref<HTMLDialogElement | null>(null)

// Native <dialog> gives focus trapping, Esc to close and an inert background for free.
onMounted(() => dialog.value?.showModal?.())
</script>

<template>
  <dialog ref="dialog" class="modal" :aria-label="title" @close="emit('close')" @cancel.prevent="emit('close')">
    <header class="modal-header">
      <h2>{{ title }}</h2>
      <button class="close" type="button" aria-label="Close" @click="emit('close')">×</button>
    </header>
    <slot />
  </dialog>
</template>

<style scoped>
.modal {
  width: min(520px, calc(100vw - 32px));
  border: none;
  border-radius: 16px;
  padding: 24px;
  box-shadow: 0 24px 60px rgba(0, 41, 64, 0.25);
  color: var(--ink);
}

.modal::backdrop {
  background: rgba(0, 41, 64, 0.45);
  backdrop-filter: blur(2px);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.close {
  border: none;
  background: none;
  font-size: 1.5rem;
  line-height: 1;
  color: var(--ink-faint);
  cursor: pointer;
}
</style>
