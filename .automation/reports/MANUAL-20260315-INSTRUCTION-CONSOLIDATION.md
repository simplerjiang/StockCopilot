# Instruction Consolidation Report / 规则收敛报告

## Plan (EN)
- Objective: simplify `.github/copilot-instructions.md` without changing rule intent.
- Approach: remove duplicated English/Chinese restatements, merge overlapping rules into grouped sections, keep unique constraints, and add one maintenance rule to merge future duplicates instead of appending more copies.
- Scope: workspace instruction cleanup only; no product logic changes.

## 计划（ZH）
- 目标：在不改变规则含义的前提下，精简 `.github/copilot-instructions.md`。
- 做法：删除重复的中英文复述，把相近规则合并进分组章节，保留仍有独立约束价值的条目，并新增一条“后续先合并再追加”的维护规则。
- 范围：仅整理工作区指令文件，不涉及业务代码逻辑。

## Development (EN)
- Rewrote `.github/copilot-instructions.md` into sectioned rules covering execution, automation, workflow, browser checks, startup safety, security, database reliability, AI/news constraints, charting, frontend tests, goal-specific delivery, and collaboration.
- Removed duplicated bilingual restatements and repeated chart/browser/startup rules while preserving the original operational requirements.
- Kept `AGENTS.md` unchanged because it was already concise and non-redundant.

## 开发结果（ZH）
- 将 `.github/copilot-instructions.md` 重组为分章节规则，覆盖执行、自动化、流程、浏览器验收、启动安全、安全与密钥、数据库可靠性、AI/资讯、图表、前端测试、目标专项规则和协作模式。
- 删除重复的中英文复述，以及重复出现的图表、浏览器、启动类规则，同时保留原有约束语义。
- `AGENTS.md` 未修改，因为它本身已经较为精简，没有明显重复块。

## Validation (EN)
- Command: diagnostics on `.github/copilot-instructions.md`
- Result: no errors found.

## 验证（ZH）
- 命令：对 `.github/copilot-instructions.md` 运行诊断检查。
- 结果：未发现错误。

## Notes (EN)
- No README, task, or state updates were required because this task only reduced instruction duplication and did not change product scope.

## 说明（ZH）
- 本次仅整理指令文本，没有新增产品范围，因此未修改 README、任务或状态文件。
