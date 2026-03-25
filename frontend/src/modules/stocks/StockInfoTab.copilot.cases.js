export const stockInfoTabCopilotCases = ({
  StockInfoTab,
  mount,
  createAbortableResponse,
  createChatFetchMock,
  createCopilotAcceptanceBaselinePayload,
  createDeferred,
  createRealtimeOverviewPayload,
  expect,
  findChatWindowForSymbol,
  findVisibleChatWindow,
  flushPromises,
  makeResponse,
  vi,
}) => [
  {
    title: "builds a stock copilot draft turn and renders timeline, tool cards, and actions",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const textarea = wrapper.find('.copilot-session-card textarea')
    await textarea.setValue('先看这只股票 60 日结构，再核对本地公告有没有新的风险点。')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const draftCall = fetchMock.mock.calls.find(args => args[0] === '/api/stocks/copilot/turns/draft')
    expect(draftCall).toBeTruthy()
    expect(JSON.parse(draftCall[1].body).symbol).toBe('sh600000')
    expect(wrapper.text()).toContain('planner 已把问题拆成 2 个受控工具步骤。')
    expect(wrapper.text()).toContain('读取 K 线结构')
    expect(wrapper.text()).toContain('StockNewsMcp')
    expect(wrapper.text()).toContain('查看本地新闻证据')
  }
  },
  {
    title: "executes approved stock copilot tools and shows evidence summaries",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    await wrapper.find('.copilot-session-card textarea').setValue('核对这只股票的本地公告。')
  await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const actionChip = wrapper.find('.copilot-action-chip')
    await actionChip.trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/mcp/news?'))).toBe(true)
    expect(wrapper.text()).toContain('本地新闻 2 条')
    expect(wrapper.text()).toContain('浦发银行关于董事会决议的公告')
    expect(wrapper.text()).toContain('上交所公告')
  }
  },
  {
    title: "renders copilot acceptance baseline and replay metrics after draft execution",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    await wrapper.find('.copilot-session-card textarea').setValue('核对这只股票的本地公告。')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    await wrapper.find('.copilot-action-chip').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => args[0] === '/api/stocks/copilot/acceptance/baseline')).toBe(true)
    expect(wrapper.text()).toContain('Copilot 质量基线')
    expect(wrapper.text()).toContain('工具效率')
    expect(wrapper.text()).toContain('Replay 基线')
    expect(wrapper.text()).toContain('4 条样本')
    expect(wrapper.text()).toContain('Evidence Traceability')
  }
  },
  {
    title: "drives chart, news, and plan workflows from copilot follow-up actions",
    run: async () => {
    const agentCalls = []
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/copilot/turns/draft') {
          const body = JSON.parse(options.body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              sessionKey: 'copilot-r3',
              title: '浦发银行 Copilot',
              turns: [
                {
                  turnId: 'turn-r3',
                  sessionKey: 'copilot-r3',
                  symbol: body.symbol,
                  userQuestion: body.question,
                  status: 'drafted',
                  plannerSummary: '先核对 K 线，再核对本地新闻，最后决定是否进入计划流。',
                  governorSummary: '仅允许本地工具，计划动作需要在工具结果齐备后解锁。',
                  planSteps: [],
                  toolCalls: [
                    {
                      callId: 'call-kline-r3',
                      toolName: 'StockKlineMcp',
                      policyClass: 'local_required',
                      purpose: '检查日 K 结构',
                      inputSummary: `symbol=${body.symbol}; interval=day; count=60`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    },
                    {
                      callId: 'call-news-r3',
                      toolName: 'StockNewsMcp',
                      policyClass: 'local_required',
                      purpose: '检查本地新闻证据',
                      inputSummary: `symbol=${body.symbol}; level=stock`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    }
                  ],
                  toolResults: [],
                  finalAnswer: {
                    status: 'done',
                    summary: '本轮已经具备 grounded final answer。',
                    needsToolExecution: false,
                    constraints: []
                  },
                  followUpActions: [
                    {
                      actionId: 'action-kline',
                      label: '查看 K 线结构',
                      actionType: 'inspect_chart',
                      toolName: 'StockKlineMcp',
                      description: '切到日 K，并刷新结构位。',
                      enabled: true,
                      blockedReason: null
                    },
                    {
                      actionId: 'action-news',
                      label: '查看新闻证据',
                      actionType: 'inspect_news',
                      toolName: 'StockNewsMcp',
                      description: '刷新本地新闻证据。',
                      enabled: true,
                      blockedReason: null
                    },
                    {
                      actionId: 'action-plan',
                      label: '起草交易计划',
                      actionType: 'draft_trading_plan',
                      toolName: '',
                      description: '把 Copilot 证据承接到交易计划。',
                      enabled: false,
                      blockedReason: '需要先完成工具执行并得到最终判断。'
                    }
                  ]
                }
              ]
            })
          })
        }

        if (String(url).startsWith('/api/stocks/mcp/kline?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              traceId: 'trace-kline-r3',
              data: {
                bars: Array.from({ length: 60 }, (_, index) => ({ index })),
                keyLevels: {
                  resistanceLevels: [10.8],
                  supportLevels: [9.7]
                }
              },
              evidence: [],
              features: [{ key: 'trendState', value: 'uptrend' }],
              warnings: [],
              degradedFlags: []
            })
          })
        }

        if (String(url).startsWith('/api/stocks/mcp/news?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              traceId: 'trace-news-r3',
              data: {
                itemCount: 1,
                latestPublishedAt: '2026-03-23T01:00:00Z'
              },
              evidence: [
                {
                  source: '上交所公告',
                  title: '浦发银行最新公告',
                  url: 'https://example.com/pfbank-notice',
                  publishedAt: '2026-03-23T01:00:00Z',
                  excerpt: '公告摘要',
                  readMode: 'full_text',
                  readStatus: 'read'
                }
              ],
              features: [{ key: 'itemCount', value: '1' }],
              warnings: [],
              degradedFlags: []
            })
          })
        }

        if (url === '/api/stocks/agents/single') {
          const body = JSON.parse(options.body)
          agentCalls.push(body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              agentId: body.agentId,
              agentName: body.agentId,
              success: true,
              data: { summary: body.agentId }
            })
          })
        }

        if (url === '/api/stocks/agents/history' && options.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              id: 88,
              symbol: 'sh600000',
              createdAt: '2026-03-23T02:00:00Z',
              isCommanderComplete: true,
              commanderBlockedReason: null
            })
          })
        }

        if (String(url).startsWith('/api/stocks/agents/history?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ([{ id: 88, symbol: 'sh600000', createdAt: '2026-03-23T02:00:00Z', isCommanderComplete: true, commanderBlockedReason: null }])
          })
        }

        if (url === '/api/stocks/plans/draft' && options.method === 'POST') {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              symbol: 'sh600000',
              name: '浦发银行',
              direction: 'Long',
              status: 'Pending',
              triggerPrice: 10.6,
              invalidPrice: 9.8,
              stopLossPrice: 9.7,
              takeProfitPrice: 11.4,
              targetPrice: 11.8,
              expectedCatalyst: '站上压力位',
              invalidConditions: '跌破关键支撑',
              riskLimits: '单笔风险 2%',
              analysisSummary: '等待量价共振确认',
              analysisHistoryId: 88,
              sourceAgent: 'commander',
              userNote: null
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    await wrapper.find('.copilot-session-card textarea').setValue('先看结构，再看新闻，最后起草交易计划。')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    expect(wrapper.find('.copilot-action-chip[data-action-id="action-plan"]').attributes('disabled')).toBeDefined()
    expect(wrapper.find('.copilot-action-chip[data-action-id="action-plan"]').attributes('title')).toContain('至少先执行一张已批准的 Copilot 工具卡')

    await wrapper.find('.copilot-action-chip[data-action-id="action-kline"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/mcp/kline?'))).toBe(true)
    expect(wrapper.find('.stock-chart-section').classes()).toContain('copilot-section-active')
    expect(wrapper.findComponent({ name: 'StockCharts' }).props('focusedView')).toBe('day')
    expect(wrapper.find('.copilot-action-chip[data-action-id="action-plan"]').attributes('title')).toContain('还有 1 张已批准工具卡未执行')

    await wrapper.find('.copilot-action-chip[data-action-id="action-news"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(fetchMock.mock.calls.some(args => String(args[0]).startsWith('/api/stocks/mcp/news?'))).toBe(true)
    expect(wrapper.find('.stock-news-impact-section').classes()).toContain('copilot-section-active')
    expect(wrapper.text()).toContain('浦发银行最新公告')

    const planAction = wrapper.find('.copilot-action-chip[data-action-id="action-plan"]')
    expect(planAction.attributes('disabled')).toBeUndefined()

    await planAction.trigger('click')
    await flushPromises()
    await flushPromises()
    await flushPromises()

    expect(agentCalls).toHaveLength(5)
    expect(fetchMock.mock.calls.some(args => args[0] === '/api/stocks/plans/draft')).toBe(true)
    expect(wrapper.find('.plan-modal').exists()).toBe(true)
    expect(wrapper.find('.stock-plan-section').classes()).toContain('copilot-section-active')
  }
  },
  {
    title: "renders cleaned copilot evidence snippets instead of navigation noise",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/copilot/turns/draft') {
          const body = JSON.parse(options.body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              sessionKey: 'copilot-r5c',
              title: '浦发银行 Copilot',
              turns: [
                {
                  turnId: 'turn-r5c',
                  sessionKey: 'copilot-r5c',
                  symbol: body.symbol,
                  userQuestion: body.question,
                  status: 'done',
                  plannerSummary: '先检查公告证据。',
                  governorSummary: '本轮只调用本地新闻工具。',
                  planSteps: [],
                  toolCalls: [
                    {
                      callId: 'call-news-r5c',
                      toolName: 'StockNewsMcp',
                      policyClass: 'local_required',
                      purpose: '检查本地新闻证据',
                      inputSummary: `symbol=${body.symbol}; level=stock`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    }
                  ],
                  toolResults: [],
                  finalAnswer: {
                    status: 'done',
                    summary: '等待新闻证据。',
                    needsToolExecution: false,
                    constraints: []
                  },
                  followUpActions: [
                    {
                      actionId: 'action-news',
                      label: '查看新闻证据',
                      actionType: 'inspect_news',
                      toolName: 'StockNewsMcp',
                      description: '刷新本地新闻证据。',
                      enabled: true,
                      blockedReason: null
                    }
                  ]
                }
              ]
            })
          })
        }

        if (String(url).startsWith('/api/stocks/mcp/news?')) {
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              traceId: 'trace-news-r5c',
              data: {
                itemCount: 1,
                latestPublishedAt: '2026-03-23T01:00:00Z'
              },
              evidence: [
                {
                  source: '上交所公告',
                  title: '浦发银行最新公告',
                  url: 'https://example.com/pfbank-notice',
                  publishedAt: '2026-03-23T01:00:00Z',
                  excerpt: '财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金',
                  summary: '公告确认本次事项未见新增重大风险，维持常规披露口径。',
                  readMode: 'full_text',
                  readStatus: 'summary_only'
                }
              ],
              features: [{ key: 'itemCount', value: '1' }],
              warnings: [],
              degradedFlags: []
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    await wrapper.find('.copilot-session-card textarea').setValue('看一下最新公告')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    await wrapper.find('.copilot-action-chip[data-action-id="action-news"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.text()).toContain('公告确认本次事项未见新增重大风险，维持常规披露口径。')
    expect(wrapper.text()).not.toContain('财经 焦点 股票 新股 期指 期权 行情 数据 全球 美股 港股 基金')
  }
  },
  {
    title: "keeps copilot draft_trading_plan disabled when final answer is not grounded done",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/copilot/turns/draft') {
          const body = JSON.parse(options.body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              sessionKey: 'copilot-r5b-final',
              title: '浦发银行 Copilot',
              turns: [
                {
                  turnId: 'turn-r5b-final',
                  sessionKey: 'copilot-r5b-final',
                  symbol: body.symbol,
                  userQuestion: body.question,
                  status: 'finalizing_answer',
                  plannerSummary: 'planner 已完成。',
                  governorSummary: 'governor 已完成。',
                  planSteps: [],
                  toolCalls: [
                    {
                      callId: 'call-kline-final',
                      toolName: 'StockKlineMcp',
                      policyClass: 'local_required',
                      purpose: '检查日 K 结构',
                      inputSummary: `symbol=${body.symbol}; interval=day; count=60`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    }
                  ],
                  toolResults: [
                    {
                      callId: 'call-kline-final',
                      toolName: 'StockKlineMcp',
                      status: 'completed',
                      summary: 'K 线结构正常。'
                    }
                  ],
                  finalAnswer: {
                    status: 'done_with_gaps',
                    summary: '证据仍有缺口。',
                    needsToolExecution: false,
                    constraints: []
                  },
                  followUpActions: [
                    {
                      actionId: 'action-plan',
                      label: '起草交易计划',
                      actionType: 'draft_trading_plan',
                      toolName: '',
                      description: '把 Copilot 证据承接到交易计划。',
                      enabled: true,
                      blockedReason: null
                    }
                  ]
                }
              ]
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()

    await wrapper.find('.copilot-session-card textarea').setValue('直接起草交易计划')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const planAction = wrapper.find('.copilot-action-chip[data-action-id="action-plan"]')
    expect(planAction.attributes('disabled')).toBeDefined()
    expect(planAction.attributes('title')).toContain('grounded final answer')
  }
  },
  {
    title: "blocks copilot draft_trading_plan when selected history is not commander complete",
    run: async () => {
    const { fetchMock } = createChatFetchMock({
      handle: async (url, options = {}) => {
        if (url === '/api/stocks/copilot/turns/draft') {
          const body = JSON.parse(options.body)
          return makeResponse({
            ok: true,
            status: 200,
            json: async () => ({
              sessionKey: 'copilot-r5b-history',
              title: '浦发银行 Copilot',
              turns: [
                {
                  turnId: 'turn-r5b-history',
                  sessionKey: 'copilot-r5b-history',
                  symbol: body.symbol,
                  userQuestion: body.question,
                  status: 'done',
                  plannerSummary: 'planner 已完成。',
                  governorSummary: 'governor 已完成。',
                  planSteps: [],
                  toolCalls: [
                    {
                      callId: 'call-kline-history',
                      toolName: 'StockKlineMcp',
                      policyClass: 'local_required',
                      purpose: '检查日 K 结构',
                      inputSummary: `symbol=${body.symbol}; interval=day; count=60`,
                      approvalStatus: 'approved',
                      blockedReason: null
                    }
                  ],
                  toolResults: [
                    {
                      callId: 'call-kline-history',
                      toolName: 'StockKlineMcp',
                      status: 'completed',
                      summary: 'K 线结构正常。'
                    }
                  ],
                  finalAnswer: {
                    status: 'done',
                    summary: '已经有 grounded answer。',
                    needsToolExecution: false,
                    constraints: []
                  },
                  followUpActions: [
                    {
                      actionId: 'action-plan',
                      label: '起草交易计划',
                      actionType: 'draft_trading_plan',
                      toolName: '',
                      description: '把 Copilot 证据承接到交易计划。',
                      enabled: true,
                      blockedReason: null
                    }
                  ]
                }
              ]
            })
          })
        }

        return null
      }
    })

    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    wrapper.vm.selectedAgentHistoryId = '3'
    wrapper.vm.agentHistoryList = [
      {
        id: 3,
        symbol: 'sh600000',
        createdAt: '2026-03-23T02:00:00Z',
        isCommanderComplete: false,
        commanderBlockedReason: '缺少指挥Agent结果，当前还不是完整的 commander 历史。'
      }
    ]
    await wrapper.vm.$nextTick()
    await flushPromises()

    await wrapper.find('.copilot-session-card textarea').setValue('起草交易计划')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const planAction = wrapper.find('.copilot-action-chip[data-action-id="action-plan"]')
    expect(planAction.attributes('disabled')).toBeDefined()
    expect(planAction.attributes('title')).toContain('指挥Agent')
  }
  },
  {
    title: "keeps recent stock copilot turns as replay chips",
    run: async () => {
    const { fetchMock } = createChatFetchMock()
    vi.stubGlobal('fetch', fetchMock)

    const wrapper = mount(StockInfoTab)
    wrapper.vm.detail = {
      quote: { name: '浦发银行', symbol: 'sh600000', price: 10.1, change: 0, changePercent: 0 },
      kLines: [],
      minuteLines: [],
      messages: []
    }
    await wrapper.vm.$nextTick()
    await flushPromises()
    await flushPromises()

    const textarea = wrapper.find('.copilot-session-card textarea')
    await textarea.setValue('第一轮问题')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    await textarea.setValue('第二轮问题')
    await wrapper.find('.copilot-session-form').trigger('submit')
    await flushPromises()
    await flushPromises()

    const replayChips = wrapper.findAll('.copilot-replay-chip')
    expect(replayChips).toHaveLength(2)
    expect(wrapper.text()).toContain('第一轮问题')
    expect(wrapper.text()).toContain('第二轮问题')
  }
  }
]
