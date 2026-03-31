import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';

const baseUrl = 'http://localhost:5119';
const userDataDir = path.resolve('..', '.automation', 'edge-profile');
const evidenceDir = path.resolve('..', '.automation', 'reports', 'market-bar-chart-fix');
fs.mkdirSync(evidenceDir, { recursive: true });

const results = [];
const log = (msg) => { console.log(msg); results.push(msg); };

const context = await chromium.launchPersistentContext(userDataDir, {
  channel: 'msedge',
  headless: true,
  viewport: { width: 1440, height: 900 }
});

try {
  const page = context.pages()[0] ?? await context.newPage();
  const consoleErrors = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });

  await page.goto(`${baseUrl}/?tab=stock-info`, { waitUntil: 'networkidle', timeout: 60000 });
  
  // Wait for market data to load - wait for idx-card to appear
  log('等待市场数据加载...');
  try {
    await page.waitForSelector('.idx-card', { timeout: 30000 });
    log('指数卡片已出现');
  } catch {
    // Maybe still loading, wait more
    log('30秒后仍无指数卡片，检查当前状态');
    const loadingText = await page.locator('.bar-loading').textContent().catch(() => null);
    const errorText = await page.locator('.bar-error').textContent().catch(() => null);
    log(`加载提示: ${loadingText}, 错误: ${errorText}`);
    await page.screenshot({ path: path.join(evidenceDir, 'timeout-no-cards.png'), fullPage: true });
    
    // Try waiting even more
    await page.waitForTimeout(10000);
    const cardCountRetry = await page.locator('.idx-card').count();
    log(`额外等待后卡片数: ${cardCountRetry}`);
  }

  await page.waitForTimeout(2000);
  await page.screenshot({ path: path.join(evidenceDir, '01-loaded.png'), fullPage: true });

  // T1: Count index cards
  const idxCards = page.locator('.idx-card');
  const cardCount = await idxCards.count();
  log(`T1-指数卡片数量: ${cardCount}`);

  if (cardCount === 0) {
    log('FATAL: 没有指数卡片，无法继续测试');
    fs.writeFileSync(path.join(evidenceDir, 'test-results.txt'), results.join('\n'));
    await context.close();
    process.exit(1);
  }

  // Collect all card names
  const cardNames = [];
  for (let i = 0; i < cardCount; i++) {
    const name = await idxCards.nth(i).locator('.idx-card-name').textContent().catch(() => '?');
    cardNames.push(name.trim());
  }
  log(`T1-卡片名称: ${cardNames.join(' | ')}`);
  const allChinese = cardNames.every(n => /[\u4e00-\u9fff]/.test(n));
  log(`T1-所有名称含中文: ${allChinese}`);

  // T2: Click first domestic index card
  log('\n--- T2: 点击第一个国内指数 ---');
  const firstCardName = await idxCards.first().locator('.idx-card-name').textContent();
  log(`T2-点击: ${firstCardName.trim()}`);
  await idxCards.first().click({ timeout: 5000 });
  
  // Wait for dialog to appear
  await page.waitForSelector('.chart-dialog', { timeout: 10000 });
  // Give chart loading time
  await page.waitForTimeout(3000);
  await page.screenshot({ path: path.join(evidenceDir, '02-popup-opened.png'), fullPage: true });

  // T3: Check popup state
  const dialogVisible = await page.locator('.chart-dialog').isVisible();
  log(`T3-弹窗可见: ${dialogVisible}`);

  const activeTabText = await page.locator('.chart-tab.active').textContent().catch(() => 'N/A');
  log(`T3-当前激活标签: ${activeTabText.trim()}`);

  const canvasCount = await page.locator('.chart-dialog canvas').count();
  log(`T3-canvas数量: ${canvasCount}`);

  const feedbackText = await page.locator('.chart-feedback').first().textContent().catch(() => null);
  log(`T3-反馈文本: ${feedbackText ?? '无(图表已渲染)'}`);

  // Check: Is it showing kline automatically (the fix)?
  const isKlineActive = activeTabText.trim().includes('日K');
  log(`T3-自动切换到日K: ${isKlineActive}`);

  // Check for blank area (the original P0 bug)
  const visibleContainers = await page.locator('.chart-container:visible').count();
  log(`T3-可见图表容器: ${visibleContainers}`);
  const hasBlankArea = canvasCount === 0 && !feedbackText;
  log(`T3-是否存在空白区域(原P0 Bug): ${hasBlankArea}`);

  // T4: Switch to minute tab
  log('\n--- T4: 切换到分时图 ---');
  await page.locator('.chart-tab:has-text("分时")').click();
  await page.waitForTimeout(2000);
  await page.screenshot({ path: path.join(evidenceDir, '03-minute-tab.png'), fullPage: true });

  const minuteActiveTab = await page.locator('.chart-tab.active').textContent();
  log(`T4-分时标签已激活: ${minuteActiveTab.trim()}`);

  const minuteCanvas = await page.locator('.chart-dialog canvas').count();
  const minuteFeedback = await page.locator('.chart-feedback').first().textContent().catch(() => null);
  log(`T4-分时canvas数: ${minuteCanvas}`);
  log(`T4-分时反馈文本: ${minuteFeedback ?? '无(分时图已渲染)'}`);
  
  // Minute data status check
  const minuteLoadingMsg = await page.locator('.chart-feedback:has-text("分时数据加载中")').isVisible().catch(() => false);
  log(`T4-分时加载中提示: ${minuteLoadingMsg}`);
  
  // If minute is still loading, wait and recheck
  if (minuteLoadingMsg) {
    log('T4-等待分时数据...');
    await page.waitForTimeout(5000);
    const stillLoading = await page.locator('.chart-feedback:has-text("分时数据加载中")').isVisible().catch(() => false);
    log(`T4-5秒后分时仍加载中: ${stillLoading}`);
  }

  // T5: Switch to kline
  log('\n--- T5: 切换到日K ---');
  await page.locator('.chart-tab:has-text("日K")').click();
  await page.waitForTimeout(1500);
  await page.screenshot({ path: path.join(evidenceDir, '04-kline-tab.png'), fullPage: true });
  const klineCanvas = await page.locator('.chart-dialog canvas').count();
  log(`T5-日K canvas数: ${klineCanvas}`);

  // T6: Close/reopen stability
  log('\n--- T6: 关闭/重开稳定性 ---');
  await page.locator('.chart-dialog-close').click();
  await page.waitForTimeout(500);
  const dialogGone = !(await page.locator('.chart-dialog-overlay').isVisible().catch(() => false));
  log(`T6-关闭成功: ${dialogGone}`);

  await idxCards.first().click({ timeout: 5000 });
  await page.waitForSelector('.chart-dialog', { timeout: 10000 });
  await page.waitForTimeout(3000);
  const reopened = await page.locator('.chart-dialog').isVisible();
  const reopenCanvas = await page.locator('.chart-dialog canvas').count();
  log(`T6-重新打开: ${reopened}, canvas: ${reopenCanvas}`);
  await page.screenshot({ path: path.join(evidenceDir, '05-reopen.png'), fullPage: true });

  await page.locator('.chart-dialog-close').click().catch(() => {});
  await page.waitForTimeout(500);

  // T7: Test global index
  log('\n--- T7: 全球指数卡片 ---');
  const globalCards = page.locator('.idx-row--global .idx-card');
  const globalCount = await globalCards.count();
  log(`T7-全球指数卡片数: ${globalCount}`);

  if (globalCount > 0) {
    const gName = await globalCards.first().locator('.idx-card-name').textContent();
    log(`T7-点击: ${gName.trim()}`);
    await globalCards.first().click({ timeout: 5000 });
    await page.waitForSelector('.chart-dialog', { timeout: 10000 });
    await page.waitForTimeout(3000);
    await page.screenshot({ path: path.join(evidenceDir, '06-global-index.png'), fullPage: true });

    const gTab = await page.locator('.chart-tab.active').textContent().catch(() => 'N/A');
    const gCanvas = await page.locator('.chart-dialog canvas').count();
    const gFeedback = await page.locator('.chart-feedback').first().textContent().catch(() => null);
    log(`T7-标签: ${gTab.trim()}, canvas: ${gCanvas}, 反馈: ${gFeedback ?? '无'}`);

    await page.locator('.chart-dialog-close').click().catch(() => {});
    await page.waitForTimeout(500);
  }

  // T7b: second global index
  if (globalCount > 1) {
    const g2Name = await globalCards.nth(1).locator('.idx-card-name').textContent();
    log(`T7b-点击: ${g2Name.trim()}`);
    await globalCards.nth(1).click({ timeout: 5000 });
    await page.waitForSelector('.chart-dialog', { timeout: 10000 });
    await page.waitForTimeout(3000);
    await page.screenshot({ path: path.join(evidenceDir, '07-global-index-2.png'), fullPage: true });

    const g2Canvas = await page.locator('.chart-dialog canvas').count();
    log(`T7b-canvas: ${g2Canvas}`);

    await page.locator('.chart-dialog-close').click().catch(() => {});
    await page.waitForTimeout(500);
  }

  // T8: Pulse chips
  log('\n--- T8: 市场脉搏 ---');
  const pulseChips = page.locator('.pulse-chip');
  const pulseCount = await pulseChips.count();
  log(`T8-脉搏芯片数: ${pulseCount}`);
  for (let i = 0; i < pulseCount; i++) {
    const label = await pulseChips.nth(i).locator('.pulse-chip-label').textContent().catch(() => '?');
    const value = await pulseChips.nth(i).locator('.pulse-chip-value').textContent().catch(() => '?');
    log(`  ${label.trim()}: ${value.trim()}`);
  }

  // T9: Expand/collapse
  log('\n--- T9: 展开/收起 ---');
  const expandBtn = page.locator('.expand-toggle');
  if (await expandBtn.count() > 0) {
    const btnText = await expandBtn.textContent();
    log(`T9-按钮: ${btnText.trim()}`);
    await expandBtn.click();
    await page.waitForTimeout(500);
    const detailVisible = await page.locator('.bar-detail-tray').isVisible().catch(() => false);
    log(`T9-展开详情: ${detailVisible}`);
    await page.screenshot({ path: path.join(evidenceDir, '08-expanded.png'), fullPage: true });
    await expandBtn.click();
    await page.waitForTimeout(500);
  }

  // T10: Errors + retry button
  log('\n--- T10: 错误与重试 ---');
  const retryBtn = await page.locator('.chart-retry-btn').count();
  log(`T10-重试按钮出现次数: ${retryBtn}`);
  
  log('\n--- T11: 控制台错误 ---');
  log(`T11-控制台错误数: ${consoleErrors.length}`);
  consoleErrors.slice(0, 10).forEach((e, i) => log(`  err${i+1}: ${e.substring(0, 180)}`));

  await page.screenshot({ path: path.join(evidenceDir, '09-final.png'), fullPage: true });

  const summary = results.join('\n');
  fs.writeFileSync(path.join(evidenceDir, 'test-results.txt'), summary);
  console.log('\n\n========== FINAL SUMMARY ==========');
  console.log(summary);
} finally {
  await context.close();
}
