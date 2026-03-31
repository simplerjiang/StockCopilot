# SimplerJiang AI Agent — UI/UX 优化升级清单

> 基于 2026-03-30 的完整应用走查生成，覆盖所有 Tab 页面和核心交互流程。

---

## 一、全局导航与布局 (App Shell)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 1.1 | 顶栏 6 个 Tab 平铺，"治理开发者模式"属于管理功能却与主业务 Tab 混排，用户认知负担大 | 将"LLM 设置"和"治理开发者模式"收入右侧齿轮/设置下拉菜单或移至独立"管理"子路由，主 Tab 只保留 4 个业务入口 | P1 | `App.vue` |
| 1.2 | 顶栏无用户头像/快捷操作区，时钟区仅一行文字，信息密度低 | 增加连接状态指示灯（后端/WebSocket）+ 通知 bell icon + 用户头像区 | P2 | `App.vue` |
| 1.3 | 版本号 `v0.0.3` 以 badge 形式挤在品牌旁边，分散注意力 | 移至设置页或 About 弹窗，顶栏只在 hover tooltip 中显示版本 | P3 | `App.vue` |
| 1.4 | 整体页面无"暗黑模式"切换（虽然顶栏是深色、内容区是白色），视觉分裂 | 实现统一的 Light/Dark 主题切换，顶栏与内容区风格一致 | P2 | `design-tokens.css`, `App.vue` |
| 1.5 | 无骨架屏(Skeleton)全局 loading 方案，Tab 切换时内容闪烁 | Tab 切换时使用全局 `<Suspense>` 或骨架占位过渡 | P2 | `App.vue`, `LoadingState.vue` |

---

## 二、股票信息页 — 搜索工具栏 (StockSearchToolbar)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 2.1 | 工具栏信息过载：来源下拉、刷新秒数、自动开关、历史自动更新等控件横向挤在一起，视觉噪音大 | 次要设置（来源/刷新间隔/历史设置）默认折叠到"⚙ 高级"弹出面板，主行只保留搜索框 + 查询按钮 | P1 | `StockSearchToolbar.vue` |
| 2.2 | "标的查询" 标题左侧有辅助文字"顶部快速切换，不再占用图表纵向空间"，这是开发说明而非用户功能文案 | 删除或改为 tooltip | P1 | `StockSearchToolbar.vue` |
| 2.3 | 历史 chip 水平滚动但无滚动指示（无渐变遮罩或箭头） | 增加左右渐变遮罩 + 箭头按钮，让用户知道可以横滚 | P2 | `StockSearchToolbar.vue` |
| 2.4 | 搜索结果下拉是自定义 div，无键盘上下选择和 Escape 关闭交互 | 补充键盘可达性：ArrowUp/Down 选中、Enter 确认、Esc 关闭 | P2 | `StockSearchToolbar.vue` |
| 2.5 | "数据刷新：手动刷新"" 历史：手动" 等状态文案在工具栏第二行，对不熟悉系统的用户缺少说明 | 将状态信息整合到搜索框右侧小图标 + tooltip，减少视觉干扰 | P3 | `StockSearchToolbar.vue` |

---

## 三、股票信息页 — 市场总览条 (StockTopMarketOverview)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 3.1 | 国内指数 3 张卡 + 全球指数 7 张卡密集排列，卡片样式一致导致信息分层不明显 | 国内指数加大号显示、全球指数缩小为 ticker-tape 滚动条或次级行 | P1 | `StockTopMarketOverview.vue` |
| 3.2 | 展开的 Detail Tray（主力/北向/广度/封板）4 个卡片占据过多纵向空间 | 改为 hover/点击弹出 popover，或收入右侧折叠面板 | P2 | `StockTopMarketOverview.vue` |
| 3.3 | 刷新倒计时圆环 SVG 太小(28px)，不容易点击到 | 增大为 36px 并加 hover 放大动效 | P3 | `StockTopMarketOverview.vue` |
| 3.4 | "休市"状态时资金数据显示"休市"文字，可用灰化 + 上一个收盘值更直观 | 休市时显示上一交易日终值 + 灰化颜色 + "休市"标签 | P2 | `StockTopMarketOverview.vue` |
| 3.5 | 个股相关性 chip 中展示了当前股票涨跌，但和上方指数卡片样式太相似，层次不清 | 用差异化颜色/边框标注当前持仓/关注股 | P3 | `StockTopMarketOverview.vue` |

---

## 四、股票信息页 — 终端看盘区 (TerminalView / StockCharts)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 4.1 | 未选股时显示"等待加载股票""选择标的以加载图表"文案过于朴素 | 改为引导性 EmptyState 组件，加入快捷搜索入口和热门股推荐按钮 | P1 | `TerminalView.vue`, `StockInfoTab.vue` |
| 4.2 | TerminalView 左上角显示 "TerminalView" 英文标签，面向中文用户不够友好 | 改为"行情终端"或直接删除该子标签 | P1 | `TerminalView.vue` |
| 4.3 | "黑白模式"按钮位置孤立在左上角，功能不明确 | 改为图标按钮(☀/🌙)放到 TerminalView header 右侧工具栏中 | P2 | `StockInfoTab.vue` |
| 4.4 | 图表区与右侧信息面板的 Splitter 分割线不够直观 | 在 splitter 中间添加微妙的双竖线图标 + hover 颜色提示 | P3 | `ResizeSplitter.vue` |
| 4.5 | K线/分时图切换使用文字按钮排列但缺少视觉活跃态区分 | 使用 segmented control(分段控件)样式，选中项有底色+微动效 | P2 | `StockCharts.vue` |

---

## 五、股票信息页 — 右侧信息面板 (SidebarTabs)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 5.1 | 右侧 4 个 Tab(交易计划/新闻影响/AI分析/全局总览)使用 emoji 作为 icon，在不同平台渲染不一致 | 替换为 SVG 图标或统一字体图标集 | P2 | `SidebarTabs.vue` |
| 5.2 | "全局总览" tab 下的"暂无交易计划"空态文案单调 | 使用 EmptyState 组件，加说明插图和 CTA 按钮 | P2 | `StockTradingPlanBoard.vue` |
| 5.3 | 交易计划卡片中按钮（编辑/取消/恢复/删除）缺少 confirm 二次确认 | 删除/取消操作增加 confirm popover 或 Modal | P1 | `StockTradingPlanSection.vue` |
| 5.4 | AI分析面板(TradingWorkbench)全屏模式的进入/退出按钮不明显 | 使用标准全屏图标 + Esc 提示 | P2 | `TradingWorkbench.vue` |
| 5.5 | 新闻影响面板的"利好/中性/利空"标签颜色区分度不够高 | 加粗色块或使用带背景的 pill badge 增强对比 | P2 | `StockNewsImpactPanel.vue` |

---

## 六、情绪轮动页 (MarketSentimentTab)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 6.1 | "混沌"阶段标签右上角独立卡片排版与左侧描述文字不协调，视觉分裂 | 将阶段指示整合为 inline badge 放在标题行右侧 | P2 | `MarketSentimentTab.vue` |
| 6.2 | 板块筛选(概念/行业/风格)、排序、比较窗口 3 个下拉并排，标签文字位于下拉上方而非左侧，录入区域过高 | 改为单行 inline 下拉组 + 紧凑标签 | P2 | `MarketSentimentTab.vue` |
| 6.3 | 情绪数据卡片（涨停/炸板率/扩散）使用白底圆角卡，但卡内字号偏大、信息密度低 | 缩小字号、增加 sparkline 小图来辅助直觉理解 | P2 | `MarketSentimentTab.vue` |
| 6.4 | 黄色 inline-alert "涨跌家数与涨跌停已自动启用实时广度补足..."说明性太强，常驻浪费空间 | 改为首次出现的 toast 或可关闭提示，第二次进入不再显示 | P3 | `MarketSentimentTab.vue` |
| 6.5 | Realtime Tape / Capital Flow / Breadth Map 三个子卡横排时没有自适应换行，小屏会被截断 | 增加 media query 在窄屏时换为垂直排列 | P2 | `MarketRealtimeOverview.vue` |

---

## 七、全量资讯库页 (NewsArchiveTab)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 7.1 | 每条资讯"查看原文"按钮右对齐但无视觉权重区分（和标签一样大小） | 改为 ghost icon-link（↗ 图标 + 文字），减少按钮噪音 | P2 | `NewsArchiveTab.vue` |
| 7.2 | 情绪/层级 badge（大盘/利好/宏观货币）颜色系统不统一 | 建立 tag 色板：层级用灰/蓝/紫，情绪用统一的红/灰/绿 | P1 | `NewsArchiveTab.vue` |
| 7.3 | 分页控件底部显示但样式朴素（仅文字按钮） | 使用标准分页组件：首页/上一页/页码/下一页/末页 | P2 | `NewsArchiveTab.vue` |
| 7.4 | 搜索框+筛选下拉的标签(搜索/层级/情绪)文字在上方而非 inline，垂直占空间 | 将标签改为 placeholder 或 inline-label | P3 | `NewsArchiveTab.vue` |
| 7.5 | 资讯列表无虚拟滚动，288 条数据分 15 页，每页 20 条渲染均为真实 DOM | 引入虚拟列表或确认分页在 20 条以下无性能问题即可 | P3 | `NewsArchiveTab.vue` |

---

## 八、股票推荐页 (StockRecommendTab)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 8.1 | 市场快照和聊天窗口上下垂直排列，快照占了首屏大半空间，聊天窗口需要滚动才能看到 | 改为左右分栏布局：左侧快照面板可折叠，右侧聊天区始终可见 | P1 | `StockRecommendTab.vue` |
| 8.2 | 聊天输入框高度过大（textarea），但初始只需单行 | 初始为单行 input，按 Shift+Enter 扩展为 textarea | P3 | `ChatWindow.vue` |
| 8.3 | "发送"按钮是全宽蓝色长条，视觉上喧宾夺主 | 改为紧凑的 icon + 文字按钮，右对齐于输入框内部 | P2 | `ChatWindow.vue` |
| 8.4 | 快捷预设按钮("每日国内外新闻"等)样式为 outline 小按钮，与下方输入区视觉割裂 | 改为 chip/pill 风格、间距和聊天消息区域统一 | P3 | `ChatWindow.vue` |
| 8.5 | 聊天历史下拉选择器较小，选中的 session 仅显示时间戳 | 增加 session 标题/摘要显示，或将多 session 切换改为侧边列表 | P2 | `StockRecommendTab.vue` |

---

## 九、LLM设置页 (AdminLlmSettings)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 9.1 | 登录表单居中但宽度仅 ~300px，页面大量留白 | 使用 centered card 布局，增加 logo/说明文字，视觉更专业 | P2 | `AdminLlmSettings.vue` |
| 9.2 | 密码输入框为 `type="text"`（从截图看无遮罩） | 改为 `type="password"` + 显示/隐藏切换按钮 | P1 | `AdminLlmSettings.vue` |
| 9.3 | 无登录错误暂无视觉反馈 | 增加红色 inline-alert 错误提示区 | P2 | `AdminLlmSettings.vue` |
| 9.4 | 登录按钮无 loading 状态 | 添加 loading spinner + 禁用态 | P2 | `AdminLlmSettings.vue` |

---

## 十、治理开发者模式页 (SourceGovernanceDeveloperMode)

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 10.1 | 页面功能复杂(治理总览/变更管理/链路追踪/LLM日志)但全部垂直堆叠，需大量滚动 | 改为左侧 sub-nav + 右侧内容区的 master-detail 布局 | P2 | `SourceGovernanceDeveloperMode.vue` |
| 10.2 | 开发者模式开关应有明确的权限提示 | 增加 toggle switch + 旁边附"仅管理员可用"标签 | P3 | `SourceGovernanceDeveloperMode.vue` |

---

## 十一、通用组件与设计系统

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 11.1 | `HelloWorld.vue` 仍存在于 components 目录，是 Vite 脚手架残留 | 删除 | P1 | `components/HelloWorld.vue` |
| 11.2 | EmptyState / ErrorState / LoadingState 组件已存在但部分页面仍然使用 `<p class="muted">暂无数据</p>` | 统一替换为 EmptyState 组件 | P2 | 多个文件 |
| 11.3 | 按钮样式不一致：部分用 `base-components.css` 的 `.btn`，部分用内联 class 如 `.market-news-button` | 统一按钮为 design system 的 btn 样式 | P2 | 多个组件 |
| 11.4 | 颜色系统 design-tokens 中定义了完整色板，但多个组件仍硬编码 hex 值(如 `#0f172a`, `#ef4444`) | 将硬编码色值替换为 CSS 变量引用 | P2 | 多个组件 |
| 11.5 | 无全局 Toast/Notification 系统，操作反馈（保存成功、刷新完成等）缺失 | 引入轻量 Toast 组件（右上角自动消失通知） | P1 | 新建 `components/Toast.vue` |
| 11.6 | 移动端适配严重不足（仅顶栏有 `@media max-width` 断点），内容区完全不响应 | 至少在 <768px 宽度下增加基础响应式适配 | P2 | 全局 |

---

## 十二、交互与体验细节

| # | 问题 | 优化建议 | 优先级 | 涉及文件 |
|---|------|---------|--------|---------|
| 12.1 | 缺少全局快捷键(如 Cmd/Ctrl+K 快速搜索股票) | 增加 Command Palette 风格的全局快速搜索 | P2 | 新功能 |
| 12.2 | 交易计划弹窗(Modal)没有动画过渡，突兀弹出 | 增加 fade + scale 进出动画 | P3 | `StockTradingPlanModal.vue` |
| 12.3 | 右键菜单(contextMenu)样式简陋，仅一个删除按钮 | 使用标准化 ContextMenu 组件，加入"复制代码""查看详情"等选项 | P3 | `StockSearchToolbar.vue` |
| 12.4 | 图表弹出窗口(MarketIndexChartPopup)没有拖拽移动能力 | 增加 header 拖拽移位 + 大小调整功能 | P3 | `MarketIndexChartPopup.vue` |
| 12.5 | 页面无 "回到顶部" 按钮 | 滚动超过一屏后显示固定的回到顶部 FAB | P3 | `App.vue` |

---

## 优先级总览

| 优先级 | 数量 | 说明 |
|--------|------|------|
| **P1** | 9 项 | 功能/体验核心问题，建议优先处理 |
| **P2** | 22 项 | 体验增强，推荐在下一迭代实现 |
| **P3** | 13 项 | 锦上添花，可排入远期 backlog |

### P1 清单速览
1. **1.1** — 管理 Tab 移入设置菜单
2. **2.1** — 搜索工具栏次要设置折叠
3. **2.2** — 删除开发说明文案
4. **3.1** — 指数区信息层次优化
5. **4.1** — 空态引导优化
6. **4.2** — "TerminalView" 改中文
7. **5.3** — 危险操作二次确认
8. **7.2** — 资讯 tag 颜色统一
9. **8.1** — 推荐页改左右分栏
10. **9.2** — 密码框输入掩码
11. **11.1** — 删除 HelloWorld.vue
12. **11.5** — 全局 Toast 通知系统
