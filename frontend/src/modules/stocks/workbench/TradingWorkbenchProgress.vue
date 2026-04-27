<script setup>
import { ref } from 'vue'

const props = defineProps({
  stages: { type: Array, default: () => [] },
  isRunning: { type: Boolean, default: false },
  error: { type: String, default: null }
})
const emit = defineEmits(['rerun-from-stage'])
const expandedRoleDetails = ref(new Set())

const parseJson = value => {
  if (!value) return null
  try { return JSON.parse(value) } catch { return null }
}

const formatDetailValue = value => {
  if (value == null || value === '') return ''
  if (typeof value === 'string') {
    const nested = parseJson(value)
    return nested == null ? value : formatDetailValue(nested)
  }
  if (Array.isArray(value)) return value.map(formatDetailValue).filter(Boolean).join('；')
  if (typeof value === 'object') {
    return Object.entries(value)
      .map(([key, val]) => `${key}: ${formatDetailValue(val)}`)
      .filter(Boolean)
      .join('；')
  }
  return String(value)
}

const getRoleOutputText = role => formatDetailValue(parseJson(role.outputContentJson)?.content ?? parseJson(role.outputContentJson))
const getRoleToolRefs = role => {
  const parsed = parseJson(role.outputRefsJson)
  return Array.isArray(parsed) ? parsed : []
}
const roleDetailKey = (stage, role, type) => `${stage.key}:${role.roleId}:${type}`
const toggleRoleDetail = key => {
  const next = new Set(expandedRoleDetails.value)
  next.has(key) ? next.delete(key) : next.add(key)
  expandedRoleDetails.value = next
}

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

const MCP_TOOL_ZH = {
  StockKlineMcp: 'K线数据', StockMinuteMcp: '分时数据', StockNewsMcp: '个股新闻',
  StockSearchMcp: '股票搜索', StockDetailMcp: '股票详情', StockProductMcp: '产品分析',
  CompanyOverviewMcp: '公司概况', MarketContextMcp: '市场背景', TechnicalMcp: '技术分析',
  StockFundamentalsMcp: '基本面', FundamentalsMcp: '基本面',
  NewsMcp: '新闻', SocialMcp: '社交舆情',
  SocialSentimentMcp: '社交情绪',
  StockShareholderMcp: '股东分析', ShareholderMcp: '股东分析',
  StockAnnouncementMcp: '公告分析', AnnouncementMcp: '公告分析',
  StockStrategyMcp: '策略分析', SectorRotationMcp: '板块轮动'
}
const FLAG_PREFIX_ZH = {
  tool_error: '工具异常',
  insufficient_evidence: '证据不足',
  timeout: '超时',
  rate_limited: '限流',
  no_data: '无数据',
  partial_data: '部分数据'
}
const translateFlag = flag => {
  if (!flag) return flag
  const idx = flag.indexOf(':')
  if (idx < 0) return FLAG_PREFIX_ZH[flag] || flag
  const prefix = flag.substring(0, idx)
  const suffix = flag.substring(idx + 1)
  const zh = FLAG_PREFIX_ZH[prefix] || prefix
  const detail = MCP_TOOL_ZH[suffix] || suffix
  return `${zh}: ${detail}`
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
             stage.status === 'Pending' ? '待执行' :
             stage.status === 'Skipped' ? '已复用' :
             stage.status === 'Degraded' ? '降级完成' : stage.status }}
        </span>
        <button
          v-if="!props.isRunning && (stage.status === 'Completed' || stage.status === 'Failed' || stage.status === 'Degraded')"
          class="wb-stage-rerun"
          :title="`从【${stage.label}】开始重新执行`"
          @click.stop="emit('rerun-from-stage', stages.indexOf(stage))"
        >
          🔄 重跑
        </button>
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
          <button
            v-if="getRoleOutputText(role)"
            class="wb-role-detail-btn"
            type="button"
            @click="toggleRoleDetail(roleDetailKey(stage, role, 'output'))"
          >输出</button>
          <button
            v-if="getRoleToolRefs(role).length"
            class="wb-role-detail-btn"
            type="button"
            @click="toggleRoleDetail(roleDetailKey(stage, role, 'tools'))"
          >MCP</button>
          <div
            v-if="expandedRoleDetails.has(roleDetailKey(stage, role, 'output'))"
            class="wb-role-detail-panel"
          >{{ getRoleOutputText(role) }}</div>
          <div
            v-if="expandedRoleDetails.has(roleDetailKey(stage, role, 'tools'))"
            class="wb-role-detail-panel"
          >
            <strong>MCP 结果</strong>
            <div v-for="(tool, toolIndex) in getRoleToolRefs(role)" :key="toolIndex">
              {{ tool.toolName || 'MCP' }} · {{ tool.status || 'Unknown' }} · {{ tool.summary || '' }}
              <span v-if="tool.resultJson"> · {{ formatDetailValue(parseJson(tool.resultJson)) }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Degraded flags -->
      <div v-if="stage.degradedFlags.length > 0" class="wb-degraded-flags">
        <span v-for="(flag, i) in stage.degradedFlags" :key="i" class="wb-degraded-flag">
          ⚠️ {{ translateFlag(flag) }}
        </span>
      </div>
    </div>

    <!-- Empty state -->
    <div v-if="props.error && stages.length === 0" class="wb-progress-empty wb-progress-failure">
      <p>研究进度加载失败</p>
      <p class="wb-progress-empty-hint">当前会话详情暂时不可用，请点击顶部刷新后重试</p>
    </div>

    <div v-else-if="stages.length === 0" class="wb-progress-empty">
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
  border: 1px solid var(--color-border-light);
  overflow: hidden;
  transition: border-color 0.2s;
}
.wb-stage.stage-running {
  border-color: var(--color-accent);
  background: color-mix(in srgb, var(--color-accent) 5%, transparent);
}
.wb-stage.stage-completed {
  border-color: var(--color-success);
  background: color-mix(in srgb, var(--color-success) 6%, transparent);
}
.wb-stage.stage-failed {
  border-color: var(--color-danger);
  background: color-mix(in srgb, var(--color-danger) 6%, transparent);
}

.wb-stage-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  font-size: 14px;
}
.wb-stage-icon { font-size: 15px; }
.wb-stage-label {
  flex: 1;
  font-weight: 500;
  color: var(--color-text-body);
}

.wb-stage-status {
  font-size: 12px;
  font-weight: 600;
  padding: 1px 6px;
  border-radius: 3px;
}
.status-running { color: var(--color-info); background: var(--color-info-bg); }
.status-completed { color: var(--color-success); background: var(--color-success-bg); }
.status-failed { color: var(--color-danger); background: var(--color-danger-bg); }
.status-pending { color: var(--color-text-secondary); }
.status-skipped { color: var(--color-text-tertiary); background: var(--color-bg-surface-alt); }

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
  flex-wrap: wrap;
  gap: 4px;
  font-size: 13px;
  color: var(--color-text-secondary);
}
.wb-role.role-completed { color: var(--color-success); }
.wb-role.role-running { color: var(--color-info); }
.wb-role.role-failed { color: var(--color-danger); }
.wb-role-icon { font-size: 12px; width: 14px; text-align: center; }
.wb-role-name { flex: 1; }
.wb-role-reused { font-size: 12px; }
.wb-role-detail-btn {
  border: 1px solid var(--color-border-light);
  background: var(--color-bg-surface-alt);
  color: var(--color-text-secondary);
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 12px;
  cursor: pointer;
}
.wb-role-detail-panel {
  flex-basis: 100%;
  margin: 3px 0 4px 18px;
  padding: 6px 8px;
  border-left: 2px solid var(--color-accent);
  background: var(--color-bg-surface-alt);
  color: var(--color-text-body);
  border-radius: 4px;
  word-break: break-word;
}

/* ── Degraded ──────────────────────────────────── */
.wb-degraded-flags {
  padding: 4px 10px 6px 28px;
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.wb-degraded-flag {
  font-size: 12px;
  color: var(--color-warning);
  background: var(--color-warning-bg);
  padding: 1px 6px;
  border-radius: 3px;
}

.wb-stage-rerun {
  background: transparent;
  border: 1px solid var(--color-accent);
  color: var(--color-accent);
  border-radius: 4px;
  padding: 1px 6px;
  font-size: 12px;
  cursor: pointer;
  flex-shrink: 0;
  transition: background 0.15s;
}
.wb-stage-rerun:hover {
  background: color-mix(in srgb, var(--color-accent) 12%, transparent);
}

.wb-stage.stage-skipped {
  border-color: var(--color-border-light);
  opacity: 0.6;
}

.wb-progress-empty {
  text-align: center;
  padding: 24px 12px;
  color: var(--color-text-secondary);
  font-size: 14px;
}
.wb-progress-empty-hint {
  margin-top: 6px;
  font-size: 13px;
}
.wb-progress-failure {
  color: var(--color-danger);
}
</style>
