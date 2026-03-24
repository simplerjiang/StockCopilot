# 2026-03-23 未解决 Bug 清单

- 说明：本文件只保留当前仍未完全解决、仍需继续验证或继续跟踪的 Bug。
- 已解决项归档：见 `.automation/buglist-resolved-20260323.md`。
- 当前开放项数量：1

## 当前开放项

### Bug 15: 顶部市场总览带卡片 UI 与 CSS 结构失衡

- 严重级别：中
- 影响范围：`frontend/src/modules/stocks/StockTopMarketOverview.vue`
- 当前结论：已在代码层确认存在多处样式实现缺陷与状态样式缺失，属于可定位的前端结构问题，不只是主观“丑”。当前 VS Code 会话无法读取 live 页面 DOM，因此最终视觉验收仍需在运行态复核。
- 已确认问题：
	- CSS 选择器过度复用：`.market-overview-belt-header`、`.stock-realtime-actions`、`.market-overview-meta`、`.market-overview-tag-row` 等共用同一组 `display:flex + justify-content:space-between`，导致按钮组、元信息行、标签行都被按“左右拉满”处理，布局意图互相污染。
	- 指数卡片样式不完整：`.market-overview-quote-card` 当前只有 `padding`，缺少独立的边框、背景、圆角层次和内部 `gap`，会让 `A 股主场 / 全球指数` 两块的信息边界发虚，局部像“裸文本”而不是卡片。
	- 占位态样式缺失：模板里有 `.is-placeholder`，但样式中没有对应定义；未选股票、隐藏状态、加载态、错误态现在主要靠普通段落文案硬撑，缺少明确的视觉反馈。
	- 信息节奏与数字排版较弱：标题、说明、meta、价格、标签、pulse 指标之间没有做更细的层级控制，且缺少数值对齐、固定宽度或 tabular 数字策略，中等宽度下容易显得挤、散、乱。
	- 响应式断点过粗：当前基本只有 `1100px` 和 `720px` 两档，三列主网格与 `minmax(160px, 1fr)` 的指标块在 800-1200 宽度区间容易发生内容拥挤、名称换行和卡片高度参差。
- 后续修复计划：
	- 拆分顶部卡片样式职责，取消当前“一组选择器覆盖多种语义区域”的写法，为 header、action、meta、tag、quote card、pulse card 建立独立样式。
	- 补齐 `.market-overview-quote-card`、`.market-overview-pulse-card`、`.is-placeholder`、loading/error/hidden 等状态样式，让所有块都有一致的卡片边界和占位语义。
	- 重做信息层级与排版：压缩无效文案噪音，强化 hero 区价格与当前标的层级，给指数与脉冲指标加入更稳定的数字对齐和间距规则。
	- 细化响应式：至少按 `3 列 -> 2 列 -> 1 列` 重排主区块，并单独处理按钮组、标签换行和全球指数列表的窄屏排布。
	- 修复后验收：重点检查“未选股票 / 已选股票 / 隐藏态 / 加载态 / 错误态”五类状态，并在桌面宽屏与窄屏各做一次页面复核。

## Bug 模板

### Bug X: 标题

- 严重级别：高 / 中 / 低
- 当前无开放项。Bug 6、Bug 7、Bug 8 已于 2026-03-24 归档到 `.automation/buglist-resolved-20260323.md`。
	- 2026-03-22 本轮在 `sh600000` 的 `盘中消息带` 与右侧 `资讯影响` 中未再看到原记录里的错字/失真标题样例。
	- 当前样本下暂未复现，但仅覆盖了 `sh600000` 单一标的，建议后续在 `全量资讯库` 再抽样复核，不在本轮直接关闭。

