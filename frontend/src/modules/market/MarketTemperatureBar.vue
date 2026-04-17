<script setup>
import { computed } from 'vue'

const props = defineProps({
  stageLabel: { type: String, default: '加载中' },
  stageScore: { type: Number, default: 0 },
  confidence: { type: Number, default: 0 },
  sessionPhase: { type: String, default: '' },
  snapshotTime: { type: String, default: '' },
  isDegraded: { type: Boolean, default: false },
  degradeReason: { type: String, default: '' },
  syncing: { type: Boolean, default: false }
})

defineEmits(['sync'])

const scoreLabel = computed(() => {
  const s = props.stageScore
  if (s < 20) return '极度恐慌'
  if (s < 35) return '恐慌'
  if (s < 45) return '谨慎'
  if (s < 55) return '中性'
  if (s < 65) return '乐观'
  if (s < 80) return '贪婪'
  return '极度贪婪'
})

const dotColor = computed(() => {
  const s = props.stageScore
  if (props.isDegraded) return '#98a6bf'
  if (s < 20) return '#1fca7a'
  if (s < 35) return '#33d889'
  if (s < 45) return '#ffaa00'
  if (s < 55) return '#c2cad8'
  if (s < 65) return '#ff8a78'
  if (s < 80) return '#ff6a67'
  return '#ff4d5e'
})

const markerLeft = computed(() => Math.min(100, Math.max(0, props.stageScore)) + '%')
const confidenceText = computed(() => {
  if (props.isDegraded && Number(props.confidence ?? 0) === 0) return '--'
  return `${Number(props.confidence ?? 0).toFixed(0)}%`
})

const timeHHmmss = computed(() => {
  if (!props.snapshotTime) return '--:--:--'
  const d = new Date(props.snapshotTime)
  if (Number.isNaN(d.getTime())) return props.snapshotTime
  return `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}:${String(d.getSeconds()).padStart(2, '0')}`
})
</script>

<template>
  <header class="tb" :class="{ degraded: isDegraded }">
    <span class="tb-dot" :style="{ background: dotColor }"></span>
    <span class="tb-stage">{{ stageLabel }}</span>
    <span v-if="sessionPhase" class="tb-phase">{{ sessionPhase }}</span>

    <div class="tb-gauge">
      <div class="tb-track">
        <div class="tb-marker" :style="{ left: markerLeft }"></div>
      </div>
      <div class="tb-labels">
        <span>极度恐慌</span><span>恐慌</span><span>谨慎</span><span>中性</span><span>乐观</span><span>贪婪</span><span>极度贪婪</span>
      </div>
    </div>

    <span class="tb-score mono">综合强度 {{ stageScore.toFixed(1) }}</span>
    <span class="tb-conf mono">置信度 {{ confidenceText }}</span>
    <span class="tb-ts mono">{{ timeHHmmss }} CST</span>

    <button class="tb-sync" @click="$emit('sync')" :disabled="syncing" :title="isDegraded ? degradeReason : '手动同步'">
      {{ syncing ? '同步中...' : '同步最新数据' }}
    </button>
  </header>
</template>

<style scoped>
.tb { display: flex; flex-wrap: wrap; align-items: center; gap: 6px 10px; min-height: 40px; padding: 6px 10px; background: #111827; border-bottom: 1px solid #334155; flex-shrink: 0; }
.tb.degraded { opacity: 0.65; filter: grayscale(0.5); }
.tb-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
.tb-stage { font-size: 14px; font-weight: 700; color: #e6eaf2; line-height: 1.3; }
.tb-phase { font-size: 11px; color: #b2bccf; line-height: 1.3; }
.tb-gauge { flex: 1 1 260px; min-width: 180px; max-width: 100%; }
.tb-track { position: relative; height: 6px; border-radius: 3px; background: linear-gradient(to right, #1ee88f 0%, #48d785 20%, #f5b35f 40%, #c2cad8 50%, #ff9b7a 65%, #ff7269 80%, #ff4d5e 100%); }
.tb-marker { position: absolute; top: -4px; width: 3px; height: 14px; background: #e6eaf2; border-radius: 1.5px; transform: translateX(-50%); box-shadow: 0 0 4px rgba(230,234,242,0.65); transition: left 0.3s ease; }
.tb-labels { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 2px 8px; margin-top: 1px; }
.tb-labels span { flex: 1 1 54px; font-size: 11px; color: #98a6bf; line-height: 1.3; text-align: center; }
.mono { font-family: Consolas, Monaco, 'Courier New', monospace; font-variant-numeric: tabular-nums; }
.tb-score { font-size: 14px; color: #e6eaf2; line-height: 1.3; }
.tb-conf { font-size: 12px; color: #b2bccf; line-height: 1.3; }
.tb-ts { font-size: 11px; color: #98a6bf; line-height: 1.3; }
.tb-sync { min-width: 102px; height: 28px; border: 1px solid #334155; background: transparent; color: #b2bccf; border-radius: 3px; cursor: pointer; font-size: 12px; flex-shrink: 0; display: flex; align-items: center; justify-content: center; padding: 0 8px; }
.tb-sync:hover:not(:disabled) { border-color: #91a4c3; color: #e6eaf2; }
.tb-sync:disabled { cursor: not-allowed; opacity: 0.45; }
@media (max-width: 820px) {
  .tb-gauge { flex-basis: 100%; order: 10; }
}
</style>
