<script setup>
defineProps({
  items: { type: Array, default: () => [] }
})
</script>

<template>
  <section class="history-strip">
    <div v-for="item in items" :key="item.tradingDate" class="history-chip" :class="{ degraded: item.isDegraded }">
      <span class="chip-date">{{ item.date }}</span>
      <strong class="chip-label">{{ item.label }}</strong>
      <small class="chip-detail">{{ item.detail }}</small>
    </div>
  </section>
</template>

<style scoped>
.history-strip {
  display: flex;
  gap: 6px;
  overflow-x: auto;
  padding: 4px 0;
}
.history-chip {
  display: grid;
  gap: 2px;
  min-width: 70px;
  padding: 6px 8px;
  background: #1a2233;
  border: 1px solid #334155;
  text-align: center;
}
.history-chip.degraded { opacity: 0.65; border-style: dashed; }
.chip-date { font-size: 12px; color: #98a6bf; line-height: 1.3; }
.chip-label { font-size: 12px; color: #e6eaf2; line-height: 1.35; }
.chip-detail { font-size: 12px; color: #b2bccf; line-height: 1.3; }
</style>
