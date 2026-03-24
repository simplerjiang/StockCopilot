import { reactive } from 'vue'

export const DOMESTIC_REALTIME_CONTEXT_SYMBOLS = ['sh000001', 'sz399001', 'sz399006']
export const GLOBAL_REALTIME_CONTEXT_SYMBOLS = ['hsi', 'hstech', 'n225', 'ndx', 'spx', 'ftse', 'ks11']

export const STOCK_LOAD_STAGE_DEFINITIONS = [
  {
    key: 'cache',
    label: '缓存回显',
    messages: {
      idle: '等待查询',
      pending: '读取本地快照',
      success: '已处理缓存结果',
      error: '缓存不可用，继续实时加载'
    }
  },
  {
    key: 'detail',
    label: 'K线/分时图表',
    messages: {
      idle: '等待查询',
      pending: '请求实时图表数据',
      success: '实时图表数据已返回',
      error: '图表数据请求失败'
    }
  },
  {
    key: 'tencent',
    label: '腾讯行情',
    messages: {
      idle: '等待查询',
      pending: '请求腾讯实时行情',
      success: '腾讯实时行情已返回',
      error: '腾讯接口请求失败'
    }
  },
  {
    key: 'eastmoney',
    label: '东方财富基本面',
    messages: {
      idle: '等待查询',
      pending: '请求东财基本面快照',
      success: '东财基本面已返回',
      error: '东财接口请求失败'
    }
  }
]

export const createStockLoadStages = () => Object.fromEntries(
  STOCK_LOAD_STAGE_DEFINITIONS.map(stage => [
    stage.key,
    {
      key: stage.key,
      label: stage.label,
      status: 'idle',
      message: stage.messages.idle
    }
  ])
)

export const createStockWorkspace = symbolKey => reactive({
  symbolKey,
  detail: null,
  loading: false,
  error: '',
  sourceLoadStages: createStockLoadStages(),
  pendingFundamentalSnapshot: undefined,
  quoteRequestToken: 0,
  detailAbortController: null,
  chatSessions: [],
  chatSessionsLoading: false,
  chatSessionsError: '',
  selectedChatSession: '',
  chatSessionsLoaded: false,
  chatSessionsRequestToken: 0,
  chatSessionsAbortController: null,
  agentResults: [],
  agentLoading: false,
  agentError: '',
  agentUpdatedAt: '',
  agentHistoryList: [],
  agentHistoryLoading: false,
  agentHistoryError: '',
  selectedAgentHistoryId: '',
  agentHistoryLoaded: false,
  agentHistoryRequestToken: 0,
  agentHistoryAbortController: null,
  newsImpact: null,
  newsImpactLoading: false,
  newsImpactError: '',
  newsImpactLoaded: false,
  newsImpactRequestToken: 0,
  newsImpactAbortController: null,
  localNewsBuckets: { stock: null, sector: null, market: null },
  localNewsLoading: false,
  localNewsError: '',
  localNewsLoaded: false,
  localNewsRequestToken: 0,
  localNewsAbortController: null,
  planDraftLoading: false,
  planSaving: false,
  planError: '',
  planModalOpen: false,
  planForm: null,
  planList: [],
  planAlerts: [],
  planListLoading: false,
  planAlertsLoading: false,
  planListLoaded: false,
  planAlertsLoaded: false,
  planListRequestToken: 0,
  planAlertsRequestToken: 0,
  planListAbortController: null,
  planAlertsAbortController: null,
  copilotQuestion: '',
  copilotAllowExternalSearch: false,
  copilotLoading: false,
  copilotError: '',
  copilotSessionKey: '',
  copilotSessionTitle: '',
  copilotCurrentTurnId: '',
  copilotReplayTurns: [],
  copilotAcceptanceBaseline: null,
  copilotAcceptanceLoading: false,
  copilotAcceptanceError: '',
  copilotAcceptanceRequestToken: 0,
  copilotAcceptanceAbortController: null,
  copilotDraftAbortController: null,
  copilotToolAbortController: null,
  copilotToolBusyCallId: '',
  copilotFocusSection: '',
  copilotChartFocusView: 'day'
})