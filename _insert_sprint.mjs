import fs from 'fs';
const path = 'c:/Users/kong/AiAgent/.automation/sprint.md';
const content = fs.readFileSync(path, 'utf8');
const marker = '## 历史归档';
const idx = content.indexOf(marker);
if (idx < 0) { console.log('MARKER NOT FOUND'); process.exit(1); }
const insert = `---

## v0.4.8 Sprint（回归测试 111 条 Bug 修复 - 交易员可信度底线）

### Sprint 目标

**2026-04-24 两轮回归测试累计发现 111 条活跃 Bug（10+ Blocker）。本 Sprint 先修 3 条最致命、直接阻断交易员信任的 Blocker 组；其余降级为 Backlog。** 详情见 [.automation/buglist.md](buglist.md)。

### Sprint 规则

- 本 Sprint 为 L 级（Dev → Test → UI Designer → User Rep 双轮验收 → 写双语报告）
- 每 Story 完成后立即 Test 验证才进下一 Story
- 任一 Story 触发新 Blocker 立即停摆，先修新 Blocker

### 活跃 Story（上限 3）

| Story | 标题 | 分级 | 覆盖 Bug | 验收标准 | 状态 |
|---|---|---|---|---|---|
| V048-S1 | 交易账务闭环一致性 | L | #88/#89/#90/#91/#92 | 持仓账务 \`成本+浮盈=市值\` 自洽；交易流水→持仓→可用资金三者联动；0 笔交易健康度显示 N/A 而非 100%；持仓行点击 drill-down 跳回股票信息并自动搜索；快速录入代码→名称自动补齐 | TODO |
| V048-S2 | 合规脱敏 & 治理隐私门禁 | L | #111/#112/#110/#63 | 治理 Trace 详情真正脱敏（不展示完整 prompt / 系统提示 / 用户追问原文）；Developer Mode 需二次确认 + 审计 log；670 次 24h 错误应触发隔离策略（阈值合理化）；SQL 注入载荷写入 lastUserIntent 前做 whitelist 正则 | TODO |
| V048-S3 | 财报数据语义完整 | L | #94/#95/#96/#107/#80 | PDF 来源标签与详情抽屉一致（有 PDF 才标 PDF）；茅台一季报明确标"累计(YTD)"或转为单季口径；关键字段缺失时不标"PDF 解析成功"；Embedding 能力不可用时股票信息/财报中心页主动显示降级横幅；PDF Q1 补齐"资产总计/负债总计"字段 | TODO |

### Backlog（本 Sprint 不做，P1 优先）

- **V048-DEBT-1**（P1 Blocker）：#71 SPA 吞 404 —— \`/api/*\` 未注册路径返 200 + index.html，必须改成 404
- **V048-DEBT-2**（P1 Blocker）：#78 \`/api/market/sync\` 5 路并发 30s 全部超时 —— 需 Semaphore + 429 throttle
- **V048-DEBT-3**（P1 Blocker）：#97 平安银行公告被绑 sh000001 上证指数 symbol
- **V048-DEBT-4**（P1 Blocker）：#82/#85 推荐 SSE 切走断连 + 单角色 >5 分钟无 ETA
- **V048-DEBT-5**（P1 Major）：#65/#68 symbol 归一 \`sh600519\` vs \`600519\` 跨接口不一致
- **V048-DEBT-6**（P1 Major）：#66/#67/#22 Research 写端点 405 + 历史 47 条全 Failed
- **V048-DEBT-7**（P1 Major）：#72 归档清洗 runId=1 卡 round_budget_reached 自动调度器失效
- **V048-DEBT-8**（P2 Major）：#69 RAG hybrid 退化为 bm25 即便 Ollama 在线
- **V048-DEBT-9**（P2 Major）：#76 Recommend lastUserIntent 中文 UTF-8/GBK 双重乱码
- **V048-DEBT-10**（P2 Minor）：#101/#55 北向资金 3 处口径打架
- **V048-DEBT-11**（P2 Minor）：#44/#103/#104 盘中消息带重复 + 凌晨时间戳无盘后标签
- **V048-DEBT-12**（P2 Minor）：#45 最近查询 \`invalid/invalid/0%\` 垃圾条目 + 无清理入口
- **V048-DEBT-13**（P3 Minor）：#48/#49 侧栏"交易计划""全局总览"Tab 空壳
- **V048-DEBT-14**（P3 Minor）：#50 推荐历史近 20 条 15 失败 4 降级 仅 1 完成
- **V048-DEBT-15**（P3 Minor）：#37/#11 LLM 幻觉标签"无荒隔靶点" + AI 标的打错
- **V048-DEBT-16**（P3 Minor）：手册缺口 4 条（财报单位断言、情绪轮动完整性、资讯标签一致、交易日志卡片收敛）
- **V048-DEBT-17**（P4 其余 60+ 条）：待 S1-S3 完成后分批排期

`;
const out = content.slice(0, idx) + insert + content.slice(idx);
fs.writeFileSync(path, out, { encoding: 'utf8' });
console.log('Inserted', insert.length, 'chars at offset', idx);
