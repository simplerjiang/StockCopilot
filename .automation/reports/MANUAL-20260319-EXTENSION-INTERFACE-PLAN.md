# Stock-and-Fund Extension Interface Adoption Plan / stock-and-fund 扩展接口吸收规划

## Plan (EN)
- Objective: inspect the public market-data endpoints used by `stock-and-fund-chrome-master`, verify which ones still work, and decide whether they should replace or supplement our current backend sources.
- Decision: do not wholesale replace our current quote stack. The extension's fastest paths are mostly Tencent and Eastmoney public endpoints that we already use in the backend for the critical quote, search, minute-line, K-line, and intraday-detail flows.
- Replacement strategy: keep the existing backend-owned dual-source architecture, then selectively absorb the extension's still-useful endpoints for batch quote, money flow, market breadth, and board ranking data.
- Exclusion: the extension author's hosted service at `110.40.187.161` is reachable but must not become a production dependency because it has no SLA, uses unstable encoding, and exposes an undocumented third-party contract.

## 计划（ZH）
- 目标：拆解 `stock-and-fund-chrome-master` 使用的公开行情接口，先验证现网是否可用，再判断它们应该“替代”还是“补充”我们现有后端数据源。
- 结论：不做整包替换。这个扩展的核心快链路本质上还是腾讯和东方财富公开端点，而这些关键链路我们后端已经接入了报价、搜索、分时、K线和逐笔明细。
- 接入策略：保留现有后端自主管理的双源骨架，再按切片吸收扩展里仍有价值的批量行情、资金流、市场广度、板块排行等接口。
- 排除项：扩展作者自建的 `110.40.187.161` 云服务虽然在线，但没有 SLA、编码表现不稳定、也没有可控契约，不能进入正式生产依赖。

## Findings (EN)
- Tencent endpoints verified online:
  - `https://qt.gtimg.cn/q=sh600000` quote payload returned `200` in about `1061 ms`.
  - `https://smartbox.gtimg.cn/s3/?v=2&t=all&c=1&q=浦发` search payload returned `200` in about `127 ms`.
- Eastmoney endpoints verified online:
  - batch quote `api/qt/ulist.np/get` returned `200` in about `148 ms`.
  - minute line `api/qt/stock/trends2/get` returned `200` in about `78 ms`.
  - K-line `push2his/api/qt/stock/kline/get` returned `200` in about `163 ms`.
  - intraday detail `api/qt/stock/details/get` returned `200` in about `35 ms`.
  - main capital flow `api/qt/stock/fflow/kline/get` returned `200` in about `142 ms`.
  - northbound flow `api/qt/kamt.rtmin/get` returned `200` in about `54 ms`.
  - breadth distribution `push2ex/getTopicZDFenBu` returned `200` in about `188 ms`.
- One extension-specific Eastmoney board-flow variant failed consistently with `基础连接已经关闭: 连接被意外关闭。`; the fuller backend-owned `EastmoneySectorRotationClient` query shape is already more robust and should remain the baseline for board ranking ingestion.
- The author's hosted service endpoints returned `200`, but response text showed encoding risk and the dependency is operationally unsafe for our stack.

## 发现（ZH）
- 腾讯接口实测可用：
  - `https://qt.gtimg.cn/q=sh600000` 报价返回 `200`，耗时约 `1061 ms`。
  - `https://smartbox.gtimg.cn/s3/?v=2&t=all&c=1&q=浦发` 搜索返回 `200`，耗时约 `127 ms`。
- 东方财富接口实测可用：
  - 批量行情 `api/qt/ulist.np/get` 返回 `200`，耗时约 `148 ms`。
  - 分时 `api/qt/stock/trends2/get` 返回 `200`，耗时约 `78 ms`。
  - K 线 `push2his/api/qt/stock/kline/get` 返回 `200`，耗时约 `163 ms`。
  - 逐笔明细 `api/qt/stock/details/get` 返回 `200`，耗时约 `35 ms`。
  - 主力资金 `api/qt/stock/fflow/kline/get` 返回 `200`，耗时约 `142 ms`。
  - 北向资金 `api/qt/kamt.rtmin/get` 返回 `200`，耗时约 `54 ms`。
  - 涨跌分布 `push2ex/getTopicZDFenBu` 返回 `200`，耗时约 `188 ms`。
- 扩展里那种简化版的东财板块资金请求持续报 `基础连接已经关闭: 连接被意外关闭。`；我们后端现有 `EastmoneySectorRotationClient` 使用的完整查询参数更稳定，后续板块排行仍应以现有实现为基线。
- 作者自建云服务虽然返回 `200`，但响应中已出现编码风险，且从运维和契约角度都不适合纳入我们的正式链路。

## Integration Plan (EN)
- R1: add backend-owned adapters for the missing public endpoints first: Eastmoney main-capital flow, northbound flow, breadth distribution, and extension-style batch quote fields where they add value.
- R2: standardize these sources behind existing service contracts with cache, timeout, retry, headers, and source-routing controls; do not let UI call third-party endpoints directly.
- R3: wire them into the existing market/stock surfaces behind feature flags, then validate with unit tests first and Browser MCP second before any default-source change.
- Guardrails: keep Tencent and Eastmoney as first-party public-source dependencies only; reject the author's hosted service, and prefer current backend query shapes over the extension's looser URL variants when the latter are unstable.

## 接入计划（ZH）
- R1：先补后端自管适配层，把目前缺失但有价值的公开端点接入进来，优先是东财主力资金、北向资金、涨跌分布，以及扩展用到的批量行情字段。
- R2：把这些来源统一收口到现有服务契约之下，补齐缓存、超时、重试、请求头与来源路由控制，不允许前端直接打第三方接口。
- R3：通过 feature flag 把新数据逐步挂到现有市场页和个股页，再按“单测优先，Browser MCP 其次”的顺序做验收，通过后再讨论默认来源切换。
- 护栏：正式依赖只保留腾讯和东方财富等公开源，不接作者个人云服务；当扩展里的 URL 形态不稳定时，以我们当前后端已经验证过的请求参数形态为准。

## Validation (EN)
- Command: PowerShell `Invoke-WebRequest` probes against Tencent, Eastmoney, and the hosted extension service endpoints.
- Result: critical public Tencent and Eastmoney endpoints returned live payloads successfully; the simplified board-flow query used by the extension failed; the hosted service responded but is rejected for production use.
- Repo comparison: source reads confirmed the backend already owns Tencent quote/search in `StockSearchService` and `TencentStockCrawler`, plus Eastmoney minute/K-line/intraday detail in `EastmoneyStockCrawler` and board ranking in `EastmoneySectorRotationClient`.

## 验证（ZH）
- 验证命令：使用 PowerShell `Invoke-WebRequest` 直接探测腾讯、东方财富和扩展作者云服务的在线端点。
- 结果：腾讯与东方财富的关键公开接口都能返回实时数据；扩展里的简化板块资金请求失败；作者云服务虽然在线，但被明确排除出正式生产方案。
- 仓库对比：源码确认我们后端已经自管腾讯搜索/报价，以及东方财富分时/K线/逐笔/板块排行等核心能力，因此本次规划是“吸收补齐”，不是“整体替换”。