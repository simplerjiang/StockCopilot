<script setup>
defineProps({
  stages: { type: Array, default: () => [] },
  isRunning: { type: Boolean, default: false }
})

const roleStatusIcon = status => {
  switch (status) {
    case 'Completed': return '✅'
    case 'Running': return '🔄'
    case 'Failed': return '❌'
    case 'Degraded': return '⚠️'
    case 'Skipped': return '⏭️'
    case 'Reused': return '♻️'
    default: return '⏳'
  }
}
</script>

<template>
  <div class="wb-progress">
    <div
      v-for="stage in stages"
      :key="stage.key"
      :class="['wb-stage', `stage-${stage.status.toLowerCase()}`]"
    >
      <div class="wb-stage-header">
        <span class="wb-stage-icon">{{ stage.icon }}</span>
        <span class="wb-stage-label">{{ stage.label }}</span>
        <span :class="['wb-stage-status', `status-${stage.status.toLowerCase()}`]">
          {{ stage.status === 'Running' ? '执行中' :
             stage.status === 'Completed' ? '完成' :
             stage.status === 'Failed' ? '失败' :
             stage.status === 'Pending' ? '待执行' : stage.status }}
        </span>
      </div>

      <!-- Role list (expanded when Running or Completed) -->
      <div v-if="stage.roles.length > 0 && (stage.status === 'Running' || stage.status === 'Completed' || stage.status === 'Failed')"
           class="wb-role-list">
        <div
          v-for="role in stage.roles"
          :key="role.roleId"
          :class="['wb-role', `role-${(role.status || 'pending').toLowerCase()}`]"
        >
          <span class="wb-role-icon">{{ roleStatusIcon(role.status) }}</span>
          <span class="wb-role-name">{{ role.roleLabel || role.roleId }}</span>
          <span v-if="role.reused" class="wb-role-reused" title="复用上轮结果">♻️</span>
        </div>
      </div>

      <!-- Degraded flags -->
      <div v-if="stage.degradedFlags.length > 0" class="wb-degraded-flags">
        <span v-for="(flag, i) in stage.degradedFlags" :key="i" class="wb-degraded-flag">
          ⚠️ {{ flag }}
        </span>
      </div>
    </div>

    <!-- Empty state -->
    <div v-if="stages.length === 0" class="wb-progress-empty">
      <p>等待研究会话启动…</p>
    </div>
  </div>
</template>

<style scoped>
.wb-progress {
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.wb-stage {
  border-radius: 6px;
  border: 1px solid var(--wb-border, #2a2d35);
  overflow: hidden;
  transition: border-color 0.2s;
}
.wb-stage.stage-running {
  border-color: var(--wb-accent, #5b9cf6);
  background: rgba(91, 156, 246, 0.05);
}
.wb-stage.stage-completed {
  border-color: #388e3c;
  background: rgba(56, 142, 60, 0.04);
}
.wb-stage.stage-failed {
  border-color: #d32f2f;
  background: rgba(211, 47, 47, 0.04);
}

.wb-stage-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  font-size: 12px;
}
.wb-stage-icon { font-size: 13px; }
.wb-stage-label {
  flex: 1;
  font-weight: 500;
  color: var(--wb-text, #e1e4ea);
}

.wb-stage-status {
  font-size: 10px;
  font-weight: 600;
  padding: 1px 6px;
  border-radius: 3px;
}
.status-running { color: #4fc3f7; background: rgba(79, 195, 247, 0.12); }
.status-completed { color: #66bb6a; background: rgba(102, 187, 106, 0.12); }
.status-failed { color: #ef5350; background: rgba(239, 83, 80, 0.12); }
.status-pending { color: #8b8fa3; }

/* ── Role list ─────────────────────────────────── */
.wb-role-list {
  padding: 2px 10px 6px 28px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.wb-role {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11px;
  color: var(--wb-text-muted, #8b8fa3);
}
.wb-role.role-completed { color: #66bb6a; }
.wb-role.role-running { color: #4fc3f7; }
.wb-role.role-failed { color: #ef5350; }
.wb-role-icon { font-size: 10px; width: 14px; text-align: center; }
.wb-role-name { flex: 1; }
.wb-role-reused { font-size: 10px; }

/* ── Degraded ──────────────────────────────────── */
.wb-degraded-flags {
  padding: 4px 10px 6px 28px;
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.wb-degraded-flag {
  font-size: 10px;
  color: #f0b429;
  background: rgba(240, 180, 41, 0.08);
  padding: 1px 6px;
  border-radius: 3px;
}

.wb-progress-empty {
  text-align: center;
  padding: 24px 12px;
  color: var(--wb-text-muted, #8b8fa3);
  font-size: 12px;
}
</style>
