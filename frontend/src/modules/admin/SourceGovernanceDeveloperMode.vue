<script setup>
import { computed, onMounted, ref, watch } from 'vue'

const DEV_MODE_KEY = 'source_governance_dev_mode'

const username = ref('')
const password = ref('')
const token = ref(localStorage.getItem('admin_token') || '')
const loginError = ref('')
const loginLoading = ref(false)

const initialDeveloperModeEnabled =
  localStorage.getItem(DEV_MODE_KEY) === '1' && Boolean(localStorage.getItem('admin_token'))
const developerModeEnabled = ref(initialDeveloperModeEnabled)
const loading = ref(false)
const errorMessage = ref('')

const overview = ref(null)
const sources = ref([])
const candidates = ref([])
const changes = ref([])
const snapshots = ref([])

const changeStatusFilter = ref('')
const changeDomainFilter = ref('')
const changeLoading = ref(false)
const changeDetail = ref(null)
const changeDetailLoading = ref(false)
const changeDetailError = ref('')

const traceId = ref('')
const traceLines = ref([])
const traceTimeline = ref([])
const traceLoading = ref(false)

const llmLogKeyword = ref('')
const llmLogTake = ref(200)
const llmLogs = ref([])
const llmLogsLoading = ref(false)
const selectedLlmLog = ref(null)

const isLoggedIn = computed(() => Boolean(token.value))
const selectedLlmRequestJson = computed(() => formatPrettyJson(extractJsonCandidate(selectedLlmLog.value?.requestText || '')))
const selectedLlmResponseJson = computed(() => formatPrettyJson(extractJsonCandidate(selectedLlmLog.value?.responseText || '')))
const selectedLlmErrorJson = computed(() => formatPrettyJson(extractJsonCandidate(selectedLlmLog.value?.errorText || '')))
const selectedLlmRawPreview = computed(() => summarizeLogText((selectedLlmLog.value?.lines || []).join('\n')))

const authHeaders = () => ({
  Authorization: `Bearer ${token.value}`
})

const handleUnauthorized = message => {
  logout()
  loginError.value = message || '登录已过期，请重新登录'
}

const login = async () => {
  loginLoading.value = true
  loginError.value = ''

  try {
    const response = await fetch('/api/admin/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: username.value, password: password.value })
    })

    if (!response.ok) {
      throw new Error('登录失败')
    }

    const data = await response.json()
    token.value = data.token
    localStorage.setItem('admin_token', token.value)
  } catch (error) {
    loginError.value = error.message || '登录失败'
  } finally {
    loginLoading.value = false
  }
}

const logout = () => {
  token.value = ''
  localStorage.removeItem('admin_token')
  developerModeEnabled.value = false
  localStorage.removeItem(DEV_MODE_KEY)
}

watch(developerModeEnabled, value => {
  localStorage.setItem(DEV_MODE_KEY, value ? '1' : '0')
})

const readJsonOrThrow = async response => {
  if (response.status === 401 || response.status === 403) {
    handleUnauthorized()
    throw new Error('unauthorized')
  }

  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || '请求失败')
  }

  return response.json()
}

const loadDashboard = async () => {
  if (!developerModeEnabled.value || !token.value) {
    return
  }

  loading.value = true
  errorMessage.value = ''

  try {
    const [overviewResp, sourcesResp, candidatesResp, errorsResp] = await Promise.all([
      fetch('/api/admin/source-governance/overview', { headers: authHeaders() }),
      fetch('/api/admin/source-governance/sources?page=1&pageSize=10', { headers: authHeaders() }),
      fetch('/api/admin/source-governance/candidates?page=1&pageSize=10', { headers: authHeaders() }),
      fetch('/api/admin/source-governance/errors?take=20', { headers: authHeaders() })
    ])

    overview.value = await readJsonOrThrow(overviewResp)
    sources.value = (await readJsonOrThrow(sourcesResp)).items || []
    candidates.value = (await readJsonOrThrow(candidatesResp)).items || []
    snapshots.value = await readJsonOrThrow(errorsResp)
    await Promise.all([loadChanges(), loadLlmLogs()])
  } catch (error) {
    if (error.message !== 'unauthorized') {
      errorMessage.value = error.message || '加载失败'
    }
  } finally {
    loading.value = false
  }
}

const loadLlmLogs = async () => {
  if (!developerModeEnabled.value || !token.value) {
    return
  }

  llmLogsLoading.value = true
  try {
    const params = new URLSearchParams({ take: String(Math.max(1, Math.min(1000, Number(llmLogTake.value) || 200))) })
    if (llmLogKeyword.value.trim()) {
      params.set('keyword', llmLogKeyword.value.trim())
    }

    const response = await fetch(`/api/admin/source-governance/llm-logs?${params.toString()}`, { headers: authHeaders() })
    const data = await readJsonOrThrow(response)
    llmLogs.value = data.items || []
  } catch (error) {
    if (error.message !== 'unauthorized') {
      errorMessage.value = error.message || '加载 LLM 对话日志失败'
    }
  } finally {
    llmLogsLoading.value = false
  }
}

const loadChanges = async () => {
  if (!developerModeEnabled.value || !token.value) {
    return
  }

  changeLoading.value = true
  try {
    const params = new URLSearchParams({ page: '1', pageSize: '20' })
    if (changeStatusFilter.value.trim()) {
      params.set('status', changeStatusFilter.value.trim())
    }
    if (changeDomainFilter.value.trim()) {
      params.set('domain', changeDomainFilter.value.trim())
    }

    const response = await fetch(`/api/admin/source-governance/changes?${params.toString()}`, { headers: authHeaders() })
    const data = await readJsonOrThrow(response)
    changes.value = data.items || []
  } catch (error) {
    if (error.message !== 'unauthorized') {
      errorMessage.value = error.message || '加载修复队列失败'
    }
  } finally {
    changeLoading.value = false
  }
}

const loadChangeDetail = async id => {
  if (!token.value) {
    return
  }

  changeDetailLoading.value = true
  changeDetailError.value = ''
  try {
    const response = await fetch(`/api/admin/source-governance/changes/${id}`, { headers: authHeaders() })
    changeDetail.value = await readJsonOrThrow(response)
  } catch (error) {
    if (error.message !== 'unauthorized') {
      changeDetailError.value = error.message || '加载变更详情失败'
    }
  } finally {
    changeDetailLoading.value = false
  }
}

const jumpToTrace = async value => {
  const normalized = String(value || '').trim()
  if (!normalized) {
    return
  }

  traceId.value = normalized
  await searchTrace()
}

const searchTrace = async () => {
  if (!token.value || !traceId.value.trim()) {
    return
  }

  traceLoading.value = true
  errorMessage.value = ''
  try {
    const response = await fetch(`/api/admin/source-governance/trace/${encodeURIComponent(traceId.value.trim())}?take=50`, { headers: authHeaders() })
    const data = await readJsonOrThrow(response)
    traceLines.value = data.lines || []
    traceTimeline.value = data.timeline || []
  } catch (error) {
    if (error.message !== 'unauthorized') {
      errorMessage.value = error.message || 'Trace 查询失败'
    }
  } finally {
    traceLoading.value = false
  }
}

const openLlmLogViewer = item => {
  selectedLlmLog.value = item
}

const closeLlmLogViewer = () => {
  selectedLlmLog.value = null
}

const summarizeLogText = value => {
  const normalized = String(value || '').replace(/\s+/g, ' ').trim()
  if (!normalized) {
    return '无内容'
  }

  return normalized.length > 180 ? `${normalized.slice(0, 180)}...` : normalized
}

const formatPrettyJson = candidate => {
  if (!candidate) {
    return ''
  }

  try {
    return JSON.stringify(JSON.parse(candidate), null, 2)
  } catch {
    return ''
  }
}

const extractJsonCandidate = raw => {
  if (!raw) {
    return ''
  }

  const trimmed = String(raw).trim()
  if (!trimmed) {
    return ''
  }

  if (isValidJson(trimmed)) {
    return trimmed
  }

  return tryExtractJson(trimmed)
}

const tryExtractJson = value => {
  const trimmed = String(value || '').trim()
  if (!trimmed) {
    return ''
  }

  if (isValidJson(trimmed)) {
    return trimmed
  }

  const firstBrace = trimmed.indexOf('{')
  const firstBracket = trimmed.indexOf('[')
  const startCandidates = [firstBrace, firstBracket].filter(index => index >= 0)
  if (startCandidates.length === 0) {
    return ''
  }

  const start = Math.min(...startCandidates)
  const endBrace = trimmed.lastIndexOf('}')
  const endBracket = trimmed.lastIndexOf(']')
  const end = Math.max(endBrace, endBracket)
  if (end <= start) {
    return ''
  }

  const candidate = trimmed.slice(start, end + 1)
  return isValidJson(candidate) ? candidate : ''
}

const isValidJson = value => {
  try {
    JSON.parse(value)
    return true
  } catch {
    return false
  }
}

onMounted(async () => {
  if (developerModeEnabled.value && token.value) {
    await loadDashboard()
  }
})
</script>

<template>
  <section class="panel">
    <h2>来源治理 Developer Mode</h2>

    <div v-if="!isLoggedIn" class="login-panel">
      <h3>管理员登录</h3>
      <div class="field">
        <label>账号</label>
        <input v-model="username" placeholder="管理员账号" />
      </div>
      <div class="field">
        <label>密码</label>
        <input v-model="password" type="password" placeholder="管理员密码" />
      </div>
      <button @click="login" :disabled="loginLoading">{{ loginLoading ? '登录中...' : '登录' }}</button>
      <p v-if="loginError" class="muted">{{ loginError }}</p>
    </div>

    <div v-else class="dev-panel">
      <div class="panel-actions">
        <label class="inline">
          <input v-model="developerModeEnabled" type="checkbox" @change="loadDashboard" />
          开启 Developer Mode（只读诊断）
        </label>
        <button class="secondary" @click="logout">退出登录</button>
      </div>

      <p v-if="!developerModeEnabled" class="muted">开启后可查看治理状态与错误快照。</p>
      <p v-if="loading" class="muted">加载中...</p>
      <p v-if="errorMessage" class="muted">{{ errorMessage }}</p>

      <template v-if="developerModeEnabled && overview">
        <section class="help-panel">
          <h3>参数说明</h3>
          <ul>
            <li><strong>Active</strong>：当前可用且被生产链路使用的来源数量。</li>
            <li><strong>Quarantine</strong>：被自动隔离的来源（解析失败率高或时效异常）。</li>
            <li><strong>Pending Candidate</strong>：待验证的新来源候选。</li>
            <li><strong>Pending Change</strong>：待发布或待验证的爬虫修复任务。</li>
            <li><strong>Rollback(7d)</strong>：过去 7 天自动回滚次数。</li>
            <li><strong>Errors(24h)</strong>：过去 24 小时验证失败 + 修复拒绝总数。</li>
            <li><strong>score</strong>：候选来源验证评分（综合可解析率、时间戳覆盖、重复率等）。</li>
            <li><strong>Trace</strong>：用于串联单次治理链路（验证、队列、执行、LLM 调用）。</li>
          </ul>
        </section>

        <div class="cards">
          <article class="card"><h4>Active</h4><p>{{ overview.activeSources }}</p></article>
          <article class="card"><h4>Quarantine</h4><p>{{ overview.quarantinedSources }}</p></article>
          <article class="card"><h4>Pending Candidate</h4><p>{{ overview.pendingCandidates }}</p></article>
          <article class="card"><h4>Pending Change</h4><p>{{ overview.pendingChanges }}</p></article>
          <article class="card"><h4>Rollback(7d)</h4><p>{{ overview.rollbackCount7d }}</p></article>
          <article class="card"><h4>Errors(24h)</h4><p>{{ overview.recentErrorCount24h }}</p></article>
        </div>

        <div class="trace-box">
          <input v-model="traceId" placeholder="输入 traceId 检索日志" />
          <button @click="searchTrace" :disabled="traceLoading">{{ traceLoading ? '检索中...' : '检索 Trace' }}</button>
        </div>
        <pre v-if="traceLines.length" class="trace-lines">{{ traceLines.join('\n') }}</pre>
        <div v-if="traceTimeline.length" class="timeline">
          <h4>Trace 时间线</h4>
          <ul>
            <li v-for="(item, idx) in traceTimeline" :key="idx">
              {{ item.occurredAt }} | {{ item.stage }} | {{ item.domain }} | {{ item.status }}
            </li>
          </ul>
        </div>

        <section class="llm-log-panel">
          <h3>LLM 对话过程日志</h3>
          <p class="muted">展示脱敏后的请求摘要、返回摘要和错误摘要；原始 prompt 与推理文本不在界面直接展示。</p>
          <div class="filters llm-filters">
            <input v-model="llmLogKeyword" placeholder="关键字过滤（traceId/provider/stage/prompt）" />
            <input v-model.number="llmLogTake" type="number" min="1" max="1000" placeholder="条数" />
            <button class="secondary" @click="loadLlmLogs" :disabled="llmLogsLoading">{{ llmLogsLoading ? '加载中...' : '刷新日志' }}</button>
          </div>
          <p v-if="llmLogsLoading" class="muted">正在读取日志...</p>
          <ul class="llm-log-list">
            <li v-for="(item, idx) in llmLogs" :key="idx" class="llm-log-item" @click="openLlmLogViewer(item)">
              <div class="llm-log-head">
                <span>{{ item.timestamp || '-' }}</span>
                <span>{{ item.level || '-' }}</span>
                <span>status={{ item.status || '-' }}</span>
                <span>provider={{ item.provider || '-' }}</span>
                <span>stages={{ (item.stages || []).join(' > ') || '-' }}</span>
                <button class="secondary" @click.stop="jumpToTrace(item.traceId)" :disabled="!item.traceId">Trace</button>
              </div>
              <pre class="llm-log-raw">请求：{{ summarizeLogText(item.requestText) }}
返回：{{ summarizeLogText(item.responseText || item.errorText) }}</pre>
            </li>
          </ul>
        </section>

        <div v-if="selectedLlmLog" class="log-viewer-overlay" @click.self="closeLlmLogViewer">
          <article class="log-viewer-dialog">
            <div class="log-viewer-header">
              <div>
                <h3>LLM 日志详情</h3>
                <p class="muted">{{ selectedLlmLog.timestamp || '-' }} | {{ selectedLlmLog.level || '-' }} | {{ selectedLlmLog.status || '-' }}</p>
              </div>
              <button class="secondary" @click="closeLlmLogViewer">关闭</button>
            </div>

            <div class="log-viewer-meta">
              <span>provider={{ selectedLlmLog.provider || '-' }}</span>
              <span>model={{ selectedLlmLog.model || '-' }}</span>
              <span>traceId={{ selectedLlmLog.traceId || '-' }}</span>
              <span>stages={{ (selectedLlmLog.stages || []).join(' > ') || '-' }}</span>
            </div>

            <section v-if="selectedLlmLog.requestText" class="log-viewer-section">
              <h4>请求摘要</h4>
              <pre class="log-viewer-raw">{{ selectedLlmLog.requestText }}</pre>
            </section>

            <section v-if="selectedLlmRequestJson" class="log-viewer-section">
              <h4>请求 JSON 美化视图</h4>
              <pre class="log-viewer-json">{{ selectedLlmRequestJson }}</pre>
            </section>

            <section v-if="selectedLlmLog.responseText" class="log-viewer-section">
              <h4>返回摘要</h4>
              <pre class="log-viewer-raw">{{ selectedLlmLog.responseText }}</pre>
            </section>

            <section v-if="selectedLlmResponseJson" class="log-viewer-section">
              <h4>返回 JSON 美化视图</h4>
              <pre class="log-viewer-json">{{ selectedLlmResponseJson }}</pre>
            </section>

            <section v-if="selectedLlmLog.errorText" class="log-viewer-section">
              <h4>异常信息</h4>
              <pre class="log-viewer-raw">{{ selectedLlmLog.errorText }}</pre>
            </section>

            <section v-if="selectedLlmErrorJson" class="log-viewer-section">
              <h4>异常 JSON 美化视图</h4>
              <pre class="log-viewer-json">{{ selectedLlmErrorJson }}</pre>
            </section>

            <section class="log-viewer-section">
              <h4>原始日志摘要</h4>
              <pre class="log-viewer-raw">{{ selectedLlmRawPreview }}</pre>
            </section>
          </article>
        </div>

        <div class="grid">
          <section>
            <h3>来源状态</h3>
            <ul>
              <li v-for="item in sources" :key="item.id">{{ item.domain }} | {{ item.status }} | {{ item.tier }}</li>
            </ul>
          </section>
          <section>
            <h3>候选来源</h3>
            <ul>
              <li v-for="item in candidates" :key="item.id">{{ item.domain }} | {{ item.status }} | score={{ item.verificationScore ?? '-' }}</li>
            </ul>
          </section>
          <section>
            <h3>修复队列</h3>
            <div class="filters">
              <input v-model="changeStatusFilter" placeholder="按状态过滤，如 generated" />
              <input v-model="changeDomainFilter" placeholder="按域名过滤，如 sina.com.cn" />
              <button class="secondary" @click="loadChanges" :disabled="changeLoading">{{ changeLoading ? '筛选中...' : '应用过滤' }}</button>
            </div>
            <ul>
              <li v-for="item in changes" :key="item.id" class="change-item">
                <span>#{{ item.id }} {{ item.domain }} | {{ item.status }} | {{ item.latestRunResult ?? 'no-run' }}</span>
                <div class="item-actions">
                  <button class="secondary" @click="loadChangeDetail(item.id)">详情</button>
                  <button class="secondary" @click="jumpToTrace(item.traceId || item.latestRunTraceId)" :disabled="!(item.traceId || item.latestRunTraceId)">跳转 Trace</button>
                </div>
              </li>
            </ul>
            <p v-if="changeDetailLoading" class="muted">加载变更详情中...</p>
            <p v-if="changeDetailError" class="muted">{{ changeDetailError }}</p>
            <article v-if="changeDetail" class="detail-panel">
              <h4>变更详情 #{{ changeDetail.id }}</h4>
              <p>状态：{{ changeDetail.status }} | 触发：{{ changeDetail.triggerReason }}</p>
              <p>Patch 数量：{{ changeDetail.patchCount }} | Replay：{{ changeDetail.proposedReplayCommand || '无' }}</p>
              <p>验证命令：{{ changeDetail.proposedTestCommand || '无' }}</p>
              <p>目标文件：{{ (changeDetail.targetFiles || []).join(', ') || '无' }}</p>
              <ul>
                <li v-for="run in (changeDetail.runs || [])" :key="run.id">
                  {{ run.executedAt }} | {{ run.result }} | {{ run.note || '-' }}
                  <button class="secondary" @click="jumpToTrace(run.traceId)" :disabled="!run.traceId">Trace</button>
                </li>
              </ul>
            </article>
          </section>
          <section>
            <h3>错误快照</h3>
            <ul>
              <li v-for="(item, idx) in snapshots" :key="idx">
                {{ item.errorType }} | {{ item.domain }} | {{ item.message }}
                <button class="secondary" @click="jumpToTrace(item.traceId)" :disabled="!item.traceId">Trace</button>
              </li>
            </ul>
          </section>
        </div>
      </template>
    </div>
  </section>
</template>

<style scoped>
.panel {
  background: #fff;
  border-radius: 16px;
  padding: 1.5rem;
  box-shadow: 0 10px 30px rgba(15, 23, 42, 0.08);
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  margin-bottom: 0.8rem;
}

.field input,
.trace-box input {
  padding: 0.6rem 0.75rem;
  border-radius: 10px;
  border: 1px solid #e2e8f0;
  font-size: 0.95rem;
}

button {
  padding: 0.6rem 1rem;
  border-radius: 999px;
  border: none;
  background: #2563eb;
  color: #fff;
  cursor: pointer;
}

button.secondary {
  background: #e2e8f0;
  color: #1f2937;
}

button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.inline {
  display: inline-flex;
  gap: 0.5rem;
  align-items: center;
}

.muted {
  color: #64748b;
}

.panel-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.card {
  border: 1px solid #e2e8f0;
  border-radius: 12px;
  padding: 0.75rem;
}

.card h4 {
  margin: 0;
  font-size: 0.85rem;
  color: #64748b;
}

.card p {
  margin: 0.4rem 0 0;
  font-size: 1.2rem;
  font-weight: 600;
}

.trace-box {
  display: flex;
  gap: 0.6rem;
  margin-bottom: 0.8rem;
}

.trace-box input {
  flex: 1;
}

.trace-lines {
  max-height: 180px;
  overflow: auto;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 0.75rem;
  background: #f8fafc;
}

.timeline {
  margin: 0.6rem 0 1rem;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 0.7rem;
}

.timeline h4 {
  margin: 0 0 0.5rem;
}

.help-panel,
.llm-log-panel {
  margin: 0.8rem 0 1rem;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 0.8rem;
  background: #fcfdff;
}

.help-panel h3,
.llm-log-panel h3 {
  margin: 0 0 0.5rem;
}

.grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 1rem;
}

.llm-filters {
  grid-template-columns: 1fr 120px auto;
}

.llm-log-list {
  max-height: 320px;
  overflow: auto;
  margin-top: 0.6rem;
}

.llm-log-item {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 0.55rem;
  margin-bottom: 0.45rem;
  cursor: pointer;
}

.llm-log-head {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 0.4rem;
  font-size: 0.85rem;
  color: #334155;
}

.llm-log-raw {
  margin: 0;
  max-height: 140px;
  overflow: auto;
  background: #f8fafc;
  border-radius: 6px;
  padding: 0.45rem;
  font-size: 0.8rem;
  white-space: pre-wrap;
  word-break: break-word;
}

.log-viewer-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.68);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1rem;
  z-index: 100;
}

.log-viewer-dialog {
  width: min(1280px, 100%);
  max-height: calc(100vh - 2rem);
  overflow: auto;
  background: #ffffff;
  border-radius: 14px;
  padding: 1rem;
  box-shadow: 0 20px 60px rgba(15, 23, 42, 0.35);
}

.log-viewer-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 1rem;
  margin-bottom: 0.8rem;
}

.log-viewer-header h3,
.log-viewer-section h4 {
  margin: 0;
}

.log-viewer-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  font-size: 0.9rem;
  color: #475569;
  margin-bottom: 0.8rem;
}

.log-viewer-section {
  margin-bottom: 1rem;
}

.log-viewer-json,
.log-viewer-raw {
  margin: 0.5rem 0 0;
  max-height: none;
  min-height: 220px;
  overflow: auto;
  background: #0f172a;
  color: #e2e8f0;
  border-radius: 8px;
  padding: 0.85rem;
  font-size: 0.85rem;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
}

.filters {
  display: grid;
  grid-template-columns: 1fr 1fr auto;
  gap: 0.5rem;
  margin-bottom: 0.6rem;
}

.change-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 0.6rem;
  margin-bottom: 0.35rem;
}

.item-actions {
  display: inline-flex;
  gap: 0.4rem;
}

.detail-panel {
  margin-top: 0.8rem;
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 0.7rem;
}

ul {
  margin: 0;
  padding-left: 1rem;
}

@media (max-width: 720px) {
  .panel-actions {
    flex-direction: column;
    align-items: flex-start;
    gap: 0.7rem;
  }

  .trace-box {
    flex-direction: column;
  }

  .filters {
    grid-template-columns: 1fr;
  }

  .llm-filters {
    grid-template-columns: 1fr;
  }

  .change-item {
    flex-direction: column;
    align-items: flex-start;
  }

  .log-viewer-header {
    flex-direction: column;
  }
}
</style>