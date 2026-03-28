<script setup>
defineProps({
  items: { type: Array, default: () => [] },
  activeTurn: { type: Object, default: null }
})

const feedTypeIcon = type => {
  const map = {
    RoleOutput: '🤖',
    ToolEvent: '🔧',
    StageTransition: '➡️',
    UserFollowUp: '👤',
    DegradedNotice: '⚠️',
    SystemMessage: 'ℹ️',
    ManagerVerdict: '👔',
    TraderProposal: '💹',
    RiskAssessment: '🛡️',
    DecisionAnnouncement: '🎯'
  }
  return map[type] ?? '📝'
}

const feedTypeLabel = type => {
  const map = {
    RoleOutput: '角色输出',
    ToolEvent: '工具调用',
    StageTransition: '阶段转换',
    UserFollowUp: '用户追问',
    DegradedNotice: '降级通知',
    SystemMessage: '系统消息',
    ManagerVerdict: '经理裁决',
    TraderProposal: '交易方案',
    RiskAssessment: '风险评估',
    DecisionAnnouncement: '投资决策'
  }
  return map[type] ?? type
}

const formatTime = ts => {
  if (!ts) return ''
  const d = new Date(ts)
  return d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

/** Group feed items by turnId. */
const groupByTurn = items => {
  const groups = []
  let currentTurn = null
  let currentGroup = null
  for (const item of items) {
    const tid = item.turnId ?? item.turn_id ?? 0
    if (tid !== currentTurn) {
      currentTurn = tid
      currentGroup = { turnId: tid, items: [] }
      groups.push(currentGroup)
    }
    currentGroup.items.push(item)
  }
  return groups
}
</script>

<template>
  <div class="wb-feed">
    <template v-if="items.length > 0">
      <div
        v-for="group in groupByTurn(items)"
        :key="group.turnId"
        class="wb-feed-turn"
      >
        <div class="wb-feed-turn-header">
          <span class="wb-feed-turn-badge">Turn {{ group.turnId }}</span>
        </div>

        <div
          v-for="(item, idx) in group.items"
          :key="idx"
          :class="['wb-feed-item', `type-${(item.type || item.feedType || 'unknown').toLowerCase()}`]"
        >
          <span class="wb-feed-icon">{{ feedTypeIcon(item.type || item.feedType) }}</span>
          <div class="wb-feed-body">
            <div class="wb-feed-meta">
              <span class="wb-feed-type">{{ feedTypeLabel(item.type || item.feedType) }}</span>
              <span v-if="item.roleId || item.role_id" class="wb-feed-role">{{ item.roleLabel || item.roleId || item.role_id }}</span>
              <span class="wb-feed-time">{{ formatTime(item.timestamp || item.createdAt) }}</span>
            </div>
            <div class="wb-feed-content">{{ item.summary || item.message || item.content || '' }}</div>
          </div>
        </div>
      </div>
    </template>

    <div v-else class="wb-feed-empty">
      <p>暂无讨论动态</p>
    </div>
  </div>
</template>

<style scoped>
.wb-feed {
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.wb-feed-turn {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.wb-feed-turn-header {
  margin-top: 4px;
}
.wb-feed-turn-badge {
  font-size: 10px;
  font-weight: 600;
  color: #b09cf6;
  background: rgba(176, 156, 246, 0.1);
  padding: 1px 8px;
  border-radius: 3px;
  font-family: 'Consolas', monospace;
}

.wb-feed-item {
  display: flex;
  gap: 6px;
  padding: 5px 8px;
  border-radius: 5px;
  background: var(--wb-card-bg, rgba(255,255,255,0.02));
  border-left: 2px solid transparent;
  transition: background 0.15s;
}
.wb-feed-item:hover {
  background: rgba(255,255,255,0.04);
}
.wb-feed-item.type-stagetransition { border-left-color: var(--wb-accent, #5b9cf6); }
.wb-feed-item.type-degradednotice { border-left-color: #f0b429; }
.wb-feed-item.type-userfollowup { border-left-color: #66bb6a; }
.wb-feed-item.type-decisionannouncement { border-left-color: #e040fb; }

.wb-feed-icon {
  font-size: 12px;
  width: 18px;
  text-align: center;
  flex-shrink: 0;
  padding-top: 1px;
}

.wb-feed-body {
  flex: 1;
  min-width: 0;
}
.wb-feed-meta {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 10px;
  color: var(--wb-text-muted, #8b8fa3);
  margin-bottom: 2px;
}
.wb-feed-type { font-weight: 600; }
.wb-feed-role {
  background: rgba(91, 156, 246, 0.1);
  padding: 0 4px;
  border-radius: 2px;
  color: var(--wb-accent, #5b9cf6);
}
.wb-feed-time { margin-left: auto; }

.wb-feed-content {
  font-size: 12px;
  color: var(--wb-text, #e1e4ea);
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
}

.wb-feed-empty {
  text-align: center;
  padding: 24px 12px;
  color: var(--wb-text-muted, #8b8fa3);
  font-size: 12px;
}
</style>
