import { chromium } from 'playwright';
const run = async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true }).catch(() => chromium.launch({ headless: true }));
  const page = await (await browser.newContext({ viewport: { width: 1600, height: 1000 } })).newPage();
  await page.goto('http://localhost:5119', { waitUntil: 'networkidle' });
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u80A1\u7968\u4FE1\u606F')) { b.click(); return; } } });
  await page.waitForTimeout(1500);
  const symbolInput = page.locator('input[placeholder]').first();
  await symbolInput.fill('600036');
  await symbolInput.press('Enter');
  await page.waitForResponse(r => r.url().includes('/api/stocks/detail'), { timeout: 120000 }).catch(() => null);
  await page.waitForTimeout(5000);
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u65B0\u95FB\u5F71\u54CD')) { b.click(); return; } } });
  await page.waitForTimeout(2000);
  
  // Tight crop of just the market news panel area
  const panelBox = await page.evaluate(() => {
    const panel = document.querySelector('.market-news-panel');
    if (!panel) return null;
    const rect = panel.getBoundingClientRect();
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height };
  });
  if (panelBox) {
    await page.screenshot({ path: '../screenshots/news-panel-crop.png', clip: panelBox });
    console.log('Panel cropped screenshot saved');
  }
  
  // Also check: are there individual stock news items?
  const stockNews = await page.evaluate(() => {
    const items = document.querySelectorAll('[class*=stock-news], [class*=impact]');
    return { count: items.length };
  });
  console.log('Stock-specific news items:', stockNews.count);
  
  // Check console errors
  const consoleErrors = [];
  page.on('console', msg => { if (msg.type() === 'error') consoleErrors.push(msg.text()); });
  await page.waitForTimeout(2000);
  console.log('Console errors:', consoleErrors.length);
  
  await browser.close();
};
run().catch(e => { console.error(e.message); process.exit(1); });
