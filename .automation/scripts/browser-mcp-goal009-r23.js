// Reusable Browser MCP validation script for GOAL-009 R2/R3.
// Usage with CopilotBrowser MCP run_code:
//   async (page) => { ...paste this function body... }
//
// Optional runtime overrides via global object before execution:
//   globalThis.__goal009 = {
//     baseUrl: 'http://localhost:5119/',
//     symbol: 'sz000021',
//     expectStage: '混沌'
//   };

async (page) => {
  const cfg = {
    baseUrl: globalThis.__goal009?.baseUrl || 'http://localhost:5119/',
    symbol: globalThis.__goal009?.symbol || 'sz000021',
    expectStage: globalThis.__goal009?.expectStage || null
  };

  const consoleErrors = [];
  const pageErrors = [];
  page.on('console', msg => {
    if (msg.type() === 'error') {
      consoleErrors.push(msg.text());
    }
  });
  page.on('pageerror', err => {
    pageErrors.push(err.message || String(err));
  });

  await page.goto(cfg.baseUrl, { waitUntil: 'domcontentloaded', timeout: 45000 });

  // R2 path: sentiment tab should expose compare-window controls and confidence text.
  await page.getByRole('button', { name: '情绪轮动' }).click({ timeout: 15000 });
  await page.waitForSelector('text=比较窗口', { timeout: 20000 });
  const compareWindow = page.getByRole('combobox', { name: '比较窗口' });
  await compareWindow.waitFor({ timeout: 20000 });
  await compareWindow.selectOption('20d');
  const sentimentText = await page.locator('body').innerText();
  const hasConfidence = sentimentText.includes('置信');
  if (!hasConfidence) {
    throw new Error('GOAL-009 R2 check failed: confidence label not found in sentiment tab');
  }

  // R3 path: open stock page and verify market-context rendering in plan cards and editor modal.
  await page.getByRole('button', { name: '股票信息' }).click({ timeout: 15000 });
  await page.waitForSelector('text=股票信息终端', { timeout: 20000 });

  const symbolInput = page.getByPlaceholder('输入股票代码/名称/拼音缩写').first();
  await symbolInput.fill(cfg.symbol);
  await symbolInput.press('Enter');

  await page.waitForSelector('text=交易计划总览', { timeout: 30000 });
  await page.waitForSelector('text=当前交易计划', { timeout: 30000 });

  const stockBody = await page.locator('body').innerText();
  const stageMatch = stockBody.match(/当前\s*(主升|分歧|退潮|混沌)/);
  const paceMatch = stockBody.match(/当前：([^\n]{1,30})/);
  const detectedStage = stageMatch ? stageMatch[1] : null;
  const detectedPace = paceMatch ? paceMatch[1] : null;

  if (!detectedStage) {
    throw new Error('GOAL-009 R3 check failed: current stage badge not found');
  }
  if (cfg.expectStage && detectedStage !== cfg.expectStage) {
    throw new Error('GOAL-009 R3 check failed: expected stage=' + cfg.expectStage + ', actual=' + detectedStage);
  }
  if (!detectedPace) {
    throw new Error('GOAL-009 R3 check failed: current pace copy not found in plan card');
  }

  await page.getByRole('button', { name: '编辑' }).first().click({ timeout: 10000 });
  await page.waitForSelector('text=交易计划草稿', { timeout: 10000 });
  await page.waitForSelector('text=市场上下文', { timeout: 10000 });

  const modalText = await page.locator('body').innerText();
  const requiredModalFields = ['阶段', '置信', '建议仓位', '节奏'];
  const missingModalFields = requiredModalFields.filter(field => !modalText.includes(field));
  if (missingModalFields.length > 0) {
    throw new Error('GOAL-009 R3 modal check failed: missing fields=' + missingModalFields.join(','));
  }

  const closeButton = page.getByRole('button', { name: '关闭' });
  if (await closeButton.count()) {
    await closeButton.first().click({ timeout: 5000 });
  }

  if (consoleErrors.length > 0) {
    throw new Error('Console errors detected: ' + consoleErrors.join(' | '));
  }
  if (pageErrors.length > 0) {
    throw new Error('Page errors detected: ' + pageErrors.join(' | '));
  }

  return {
    ok: true,
    baseUrl: cfg.baseUrl,
    symbol: cfg.symbol,
    expectStage: cfg.expectStage,
    detectedStage,
    detectedPace,
    checks: {
      sentimentCompareWindow: true,
      sentimentConfidence: true,
      planCurrentStage: detectedStage,
      planCurrentPace: detectedPace,
      modalMarketContextFields: requiredModalFields
    },
    errors: {
      console: consoleErrors,
      page: pageErrors
    }
  };
};
