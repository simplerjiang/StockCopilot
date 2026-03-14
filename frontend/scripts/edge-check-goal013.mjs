import { chromium } from 'playwright';
import fs from 'node:fs';

const baseUrl = process.env.GOAL013_BASE_URL || 'http://localhost:5119';
const backendLogPath = process.env.GOAL013_BACKEND_LOG || 'backend/SimplerJiangAiAgent.Api/App_Data/logs/llm-requests.txt';

const measureTerminalLayout = () => {
  const terminal = document.querySelector('.terminal-view');
  const wrappers = Array.from(document.querySelectorAll('.chart-wrapper'));
  const charts = Array.from(document.querySelectorAll('.chart'));

  return {
    terminalWidth: terminal?.getBoundingClientRect().width ?? 0,
    wrapperWidths: wrappers.map(node => node.getBoundingClientRect().width),
    chartWidths: charts.map(node => node.getBoundingClientRect().width),
    chartOverflow: wrappers.some((wrapper, index) => {
      const chart = charts[index];
      if (!chart) {
        return false;
      }
      return chart.getBoundingClientRect().width - wrapper.getBoundingClientRect().width > 1;
    })
  };
};

const run = async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true }).catch(async () => {
    return chromium.launch({ headless: true });
  });

  const context = await browser.newContext();
  const page = await context.newPage();

  const consoleErrors = [];
  page.on('console', msg => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  });

  const responseErrors = [];
  const localNewsResponses = [];
  const archiveResponses = [];
  page.on('response', async resp => {
    const url = resp.url();
    if (!url.includes('/api/news') && !url.includes('/api/stocks/detail') && !url.includes('/api/stocks/news/impact')) {
      return;
    }

    if (resp.status() >= 400) {
      responseErrors.push(`${resp.status()} ${url}`);
      return;
    }

    if (url.includes('/api/news')) {
      try {
        const json = await resp.json();
        if (url.includes('/api/news/archive')) {
          archiveResponses.push({ url, json });
        } else {
          localNewsResponses.push({ url, json });
        }
      } catch {
        if (url.includes('/api/news/archive')) {
          archiveResponses.push({ url, json: null });
        } else {
          localNewsResponses.push({ url, json: null });
        }
      }
    }
  });

  await page.goto(baseUrl, { waitUntil: 'networkidle' });
  await page.getByRole('button', { name: '股票信息' }).click({ force: true });

  const marketNewsReady = Promise.race([
    page.waitForResponse(resp => resp.url().includes('/api/news') && resp.url().includes('level=market') && resp.status() === 200, { timeout: 30000 }).catch(() => null),
    page.waitForFunction(() => document.querySelectorAll('.market-news-panel .market-news-item').length >= 1, null, { timeout: 30000 }).then(() => null)
  ]);

  const symbolInput = page.getByPlaceholder('输入股票代码/名称/拼音缩写');
  const detailResponse = page.waitForResponse(resp => resp.url().includes('/api/stocks/detail') && resp.status() === 200, { timeout: 90000 });
  const stockNewsResponse = page.waitForResponse(resp => resp.url().includes('/api/news') && resp.url().includes('level=stock') && resp.status() === 200, { timeout: 90000 });
  const sectorNewsResponse = page.waitForResponse(resp => resp.url().includes('/api/news') && resp.url().includes('level=sector') && resp.status() === 200, { timeout: 90000 });
  await symbolInput.fill('600000');
  await symbolInput.press('Enter');
  await Promise.all([detailResponse, stockNewsResponse, sectorNewsResponse, marketNewsReady]);

  await page.waitForSelector('text=股票信息终端', { timeout: 20000 });
  await page.waitForFunction(() => document.querySelectorAll('.news-bucket-card').length >= 2, null, { timeout: 30000 });
  await page.waitForFunction(() => document.querySelectorAll('.news-bucket-card li').length >= 1, null, { timeout: 30000 });
  await page.waitForFunction(() => document.querySelectorAll('.market-news-panel .market-news-item').length >= 1, null, { timeout: 30000 });
  await page.waitForSelector('.run-standard-button', { timeout: 20000 });
  await page.waitForSelector('.run-pro-button', { timeout: 20000 });
  await page.waitForTimeout(2000);

  const expandedLayout = await page.evaluate(measureTerminalLayout);
  await page.getByRole('button', { name: '专注模式' }).click({ force: true });
  await page.waitForFunction(() => document.querySelector('.workspace-grid')?.classList.contains('focused') === true, null, { timeout: 10000 });
  await page.waitForTimeout(800);
  const focusedLayout = await page.evaluate(measureTerminalLayout);

  await page.getByRole('button', { name: '显示 AI 侧栏' }).click({ force: true });
  await page.waitForFunction(() => document.querySelector('.workspace-grid')?.classList.contains('focused') === false, null, { timeout: 10000 });
  await page.waitForTimeout(800);
  const restoredLayout = await page.evaluate(measureTerminalLayout);

  const quoteText = await page.locator('.quote-card').first().textContent();
  const stockFactCount = await page.locator('.news-bucket-card').first().locator('li').count();
  const sentimentBadgeCount = await page.locator('.news-bucket-card .impact-tag').count();
  const marketFactCount = await page.locator('.market-news-panel .market-news-item').count();
  const proButtonText = await page.locator('.run-pro-button').textContent();
  const clickableTicker = page.locator('.messages li.clickable').first();
  if (await clickableTicker.count()) {
    const popupPromise = context.waitForEvent('page').catch(() => null);
    await clickableTicker.click({ force: true });
    const popup = await popupPromise;
    if (popup) {
      await popup.close();
    }
  }
  const pageContent = await page.content();

  const archiveResponse = page.waitForResponse(resp => resp.url().includes('/api/news/archive') && resp.status() === 200, { timeout: 90000 });
  await page.getByRole('button', { name: '全量资讯库' }).click({ force: true });
  await archiveResponse;
  await page.waitForSelector('text=全量资讯库', { timeout: 20000 });
  await page.waitForSelector('.archive-toolbar', { timeout: 20000 });
  await page.getByPlaceholder('标题 / 译题 / 代码 / 板块 / 靶点').fill('市场');
  const archiveSearchResponse = page.waitForResponse(resp => resp.url().includes('/api/news/archive') && resp.url().includes('keyword=%E5%B8%82%E5%9C%BA') && resp.status() === 200, { timeout: 90000 });
  await page.getByRole('button', { name: '检索' }).click({ force: true });
  await archiveSearchResponse;
  await page.waitForSelector('.archive-pagination', { timeout: 20000 });
  const archiveCardCount = await page.locator('.archive-card').count();
  const archiveBadgeCount = await page.locator('.archive-card .archive-badge').count();

  await browser.close();

  if (!quoteText || !quoteText.includes('600000')) {
    throw new Error(`Expected queried stock quote, got: ${quoteText ?? '<empty>'}`);
  }

  if (stockFactCount < 1) {
    throw new Error('Expected at least one local stock fact item after query');
  }

  if (sentimentBadgeCount < 1) {
    throw new Error('Expected sentiment badges in local news buckets');
  }

  if (marketFactCount < 1) {
    throw new Error('Expected at least one market fact item in the embedded market news panel');
  }

  if (!proButtonText || !proButtonText.includes('Pro')) {
    throw new Error(`Expected Pro analysis button text, got: ${proButtonText ?? '<empty>'}`);
  }

  if (localNewsResponses.length < 3) {
    throw new Error(`Expected 3 local news responses, got ${localNewsResponses.length}`);
  }

  if (expandedLayout.chartOverflow || focusedLayout.chartOverflow || restoredLayout.chartOverflow) {
    throw new Error('Detected chart overflow beyond chart-wrapper bounds after sidebar resize');
  }

  if (focusedLayout.terminalWidth <= expandedLayout.terminalWidth + 80) {
    throw new Error(`Expected terminal width to grow after hiding AI sidebar, before=${expandedLayout.terminalWidth}, after=${focusedLayout.terminalWidth}`);
  }

  if (restoredLayout.terminalWidth >= focusedLayout.terminalWidth - 80) {
    throw new Error(`Expected terminal width to shrink after reopening AI sidebar, focused=${focusedLayout.terminalWidth}, restored=${restoredLayout.terminalWidth}`);
  }

  if (focusedLayout.wrapperWidths.some((width, index) => width <= (expandedLayout.wrapperWidths[index] ?? 0) + 40)) {
    throw new Error(`Expected chart wrappers to expand after hiding AI sidebar. before=${expandedLayout.wrapperWidths.join(',')} after=${focusedLayout.wrapperWidths.join(',')}`);
  }

  if (restoredLayout.wrapperWidths.some((width, index) => width >= (focusedLayout.wrapperWidths[index] ?? 0) - 40)) {
    throw new Error(`Expected chart wrappers to shrink after reopening AI sidebar. focused=${focusedLayout.wrapperWidths.join(',')} restored=${restoredLayout.wrapperWidths.join(',')}`);
  }

  if (!pageContent.includes('个股事实') || !pageContent.includes('板块上下文') || !pageContent.includes('大盘资讯')) {
    throw new Error('Expected local news sections not found in the updated Step 2.3 layout');
  }

  if (archiveResponses.length < 2) {
    throw new Error(`Expected archive API responses, got ${archiveResponses.length}`);
  }

  if (archiveCardCount < 1) {
    throw new Error('Expected at least one archive card after loading the news archive tab');
  }

  if (archiveBadgeCount < 2) {
    throw new Error('Expected archive badges for level/sentiment/tags in the news archive tab');
  }

  if (consoleErrors.length > 0) {
    throw new Error(`Console errors found: ${consoleErrors.join(' | ')}`);
  }

  if (responseErrors.length > 0) {
    throw new Error(`API errors found: ${responseErrors.join(' | ')}`);
  }

  if (fs.existsSync(backendLogPath)) {
    const logs = fs.readFileSync(backendLogPath, 'utf8').toLowerCase();
    if (logs.includes('unhandled exception') || logs.includes('fatal')) {
      throw new Error(`Backend log contains critical errors. log=${backendLogPath}`);
    }
  }

  console.log('GOAL-013 edge check passed');
};

run().catch(err => {
  console.error(err);
  process.exit(1);
});