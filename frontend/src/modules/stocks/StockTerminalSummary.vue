<script setup>
import { computed } from 'vue'
import StockSourceLoadProgress from './StockSourceLoadProgress.vue'

const props = defineProps({
  detail: {
    type: Object,
    default: null
  },
  showSourceLoadProgress: {
    type: Boolean,
    default: false
  },
  sourceLoadProgressTitle: {
    type: String,
    default: ''
  },
  sourceLoadProgressPercent: {
    type: Number,
    default: 0
  },
  visibleSourceLoadStages: {
    type: Array,
    default: () => []
  },
  formatDate: {
    type: Function,
    required: true
  }
})

defineEmits(['open-external'])

const visibleMessages = computed(() => {
  const messages = props.detail?.messages
  if (Array.isArray(messages)) return messages
  if (Array.isArray(messages?.messages)) return messages.messages
  return []
})

const messagesDegraded = computed(() => Boolean(
  props.detail?.messagesDegraded ?? props.detail?.messages?.degraded ?? false
))

const messagesWarning = computed(() => String(
  props.detail?.warning ?? props.detail?.messages?.warning ?? ''
).trim())

const messageEmptyText = computed(() => (
  messagesDegraded.value
    ? '盘中消息暂不可用，已降级为空态展示。'
    : '暂无盘中消息。'
))

const formatFactSource = fact => String(fact?.source ?? '').trim()

const shanghaiTimeFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: 'Asia/Shanghai',
  weekday: 'short',
  hour: '2-digit',
  minute: '2-digit',
  hour12: false
})

const getShanghaiTimeParts = value => {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  const parts = Object.fromEntries(shanghaiTimeFormatter.formatToParts(date).map(part => [part.type, part.value]))
  const hour = Number(parts.hour)
  const minute = Number(parts.minute)
  if (!Number.isFinite(hour) || !Number.isFinite(minute)) return null
  return { weekday: parts.weekday, minutes: hour * 60 + minute }
}

const getMessageTradingSessionLabel = item => {
  const parts = getShanghaiTimeParts(item?.publishedAt ?? item?.PublishedAt)
  if (!parts) return ''

  if (parts.weekday === 'Sat' || parts.weekday === 'Sun') return '非交易时段'
  if (parts.minutes < 9 * 60 + 30) return '盘前'
  if (parts.minutes > 15 * 60) return '盘后'
  if (parts.minutes > 11 * 60 + 30 && parts.minutes < 13 * 60) return '非交易时段'
  return ''
}
</script>

<template>
  <div v-if="detail" class="terminal-summary-grid">
    <div class="quote-card">
      <p><strong>{{ detail.quote.name }}</strong>（{{ detail.quote.symbol }}）</p>
      <p>当前价：{{ detail.quote.price }}</p>
      <p>涨跌：{{ detail.quote.change }}（{{ detail.quote.changePercent }}%）</p>
      <p class="muted">更新时间：{{ formatDate(detail.quote.timestamp) }}</p>
      <StockSourceLoadProgress
        v-if="showSourceLoadProgress"
        :title="sourceLoadProgressTitle"
        :progress-percent="sourceLoadProgressPercent"
        :stages="visibleSourceLoadStages"
      />
    </div>

    <div class="quote-card">
      <div class="quote-card-header">
        <h4>基本面快照</h4>
      </div>
      <p>流通市值：{{ detail.quote.floatMarketCap ? `${(Number(detail.quote.floatMarketCap) / 100000000).toFixed(2)} 亿` : '-' }}</p>
      <p>市盈率：{{ detail.quote.peRatio ?? '-' }}</p>
      <p>量比：{{ detail.quote.volumeRatio ?? '-' }}</p>
      <p>股东户数：{{ detail.quote.shareholderCount ? Number(detail.quote.shareholderCount).toLocaleString('zh-CN') : '-' }}</p>
      <p>所属板块：{{ detail.quote.sectorName || '-' }}</p>
      <p v-if="detail.fundamentalSnapshot?.updatedAt" class="muted">快照刷新：{{ formatDate(detail.fundamentalSnapshot.updatedAt) }}</p>
      <ul v-if="detail.fundamentalSnapshot?.facts?.length" class="fundamental-facts">
        <li v-for="fact in detail.fundamentalSnapshot.facts.slice(0, 8)" :key="`fundamental-${fact.label}-${fact.value}-${fact.source ?? ''}`">
          <span><strong>{{ fact.label }}：</strong>{{ fact.value }}</span>
          <small v-if="formatFactSource(fact)" class="fundamental-fact-source">口径：{{ formatFactSource(fact) }}</small>
        </li>
      </ul>
    </div>

    <div class="quote-card tape-card">
      <div class="quote-card-header">
        <h4>盘中消息带</h4>
        <div class="message-tape-status">
          <span v-if="messagesDegraded" class="message-degraded-badge">消息降级</span>
          <span class="muted">{{ visibleMessages.length }} 条</span>
        </div>
      </div>
      <p v-if="messagesDegraded && messagesWarning" class="message-degraded-warning">{{ messagesWarning }}</p>
      <ul v-if="visibleMessages.length" class="messages">
        <li
          v-for="item in visibleMessages"
          :key="`${item.title}-${item.publishedAt ?? item.PublishedAt ?? ''}`"
          :class="{ clickable: !!(item.url ?? item.Url) }"
          @click="$emit('open-external', item.url ?? item.Url)"
        >
          <span>{{ item.title }}</span>
          <small>
            {{ item.source }} · 发布时间 {{ formatDate(item.publishedAt ?? item.PublishedAt) }}
            <template v-if="getMessageTradingSessionLabel(item)"> · {{ getMessageTradingSessionLabel(item) }}</template>
          </small>
        </li>
      </ul>
      <p v-else class="muted" :class="{ 'message-degraded-empty': messagesDegraded }">{{ messageEmptyText }}</p>
    </div>
  </div>

  <div v-else class="terminal-empty stock-terminal-empty">
    <h4>等待加载股票</h4>
    <p>主视区只保留价格、分时、K 线、量价指标与消息带。</p>
    <p class="muted">数据来源：腾讯 / 新浪 / 百度（后端爬虫占位）</p>
    <StockSourceLoadProgress
      v-if="showSourceLoadProgress"
      :title="sourceLoadProgressTitle"
      :progress-percent="sourceLoadProgressPercent"
      :stages="visibleSourceLoadStages"
      empty
    />
  </div>
</template>

<style scoped>
.terminal-summary-grid {
  display: grid;
  gap: 1rem;
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.quote-card {
  padding: 1rem;
  border-radius: 16px;
  background: rgba(15, 23, 42, 0.34);
  border: 1px solid rgba(148, 163, 184, 0.12);
}

.quote-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
}

.quote-card-header h4 {
  margin: 0;
}

.message-tape-status {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.message-degraded-badge {
  display: inline-flex;
  align-items: center;
  min-height: 1.45rem;
  padding: 0.1rem 0.45rem;
  border-radius: 999px;
  background: rgba(245, 158, 11, 0.16);
  color: #92400e;
  border: 1px solid rgba(245, 158, 11, 0.35);
  font-size: 0.78rem;
  font-weight: 700;
  white-space: nowrap;
}

.message-degraded-warning {
  margin: 0.65rem 0 0;
  color: #92400e;
  font-size: 0.88rem;
  line-height: 1.45;
}

.message-degraded-empty {
  color: #92400e;
}

.quote-card p,
.terminal-empty p {
  margin: 0.2rem 0;
}

.fundamental-facts {
  margin: 0.45rem 0 0;
  padding-left: 1rem;
  display: grid;
  gap: 0.3rem;
}

.fundamental-facts li {
  color: #d9d4c7;
  display: grid;
  gap: 0.12rem;
}

.fundamental-fact-source {
  color: #94a3b8;
  font-size: 0.78rem;
  line-height: 1.35;
}

.tape-card {
  min-width: 0;
}

.stock-terminal-empty {
  display: grid;
  gap: 0.35rem;
  min-height: 140px;
  align-content: center;
}

.stock-terminal-empty h4 {
  margin: 0;
  color: #f8fafc;
}

.messages {
  list-style: none;
  padding: 0;
  margin: 0.65rem 0 0;
  display: grid;
  gap: 0.55rem;
  max-height: 18rem;
  overflow-y: auto;
  padding-right: 0.25rem;
}

.messages li {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  padding-bottom: 0.55rem;
  border-bottom: 1px solid rgba(148, 163, 184, 0.12);
  color: #4b5563;
  font-size: 0.9rem;
}

.messages li.clickable {
  cursor: pointer;
}

.messages li.clickable:hover {
  color: #0f172a;
}

.messages small {
  color: #94a3b8;
}

@media (max-width: 1180px) {
  .terminal-summary-grid {
    grid-template-columns: 1fr;
  }
}

@media (max-width: 720px) {
  .quote-card-header {
    grid-template-columns: 1fr;
  }
}
</style>