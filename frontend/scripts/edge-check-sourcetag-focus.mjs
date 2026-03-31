import { chromium } from 'playwright';

const run = async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true }).catch(() => chromium.launch({ headless: true }));
  const page = await (await browser.newContext({ viewport: { width: 1600, height: 1000 } })).newPage();
  await page.goto('http://localhost:5119', { waitUntil: 'networkidle' });
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u80A1\u7968\u4FE1\u606F')) { b.click(); return; } } });
  await page.waitForTimeout(1500);
  const symbolInput = page.locator('input[placeholder]').first();
  await symbolInput.fill('600000');
  await symbolInput.press('Enter');
  await page.waitForResponse(r => r.url().includes('/api/stocks/detail'), { timeout: 120000 }).catch(() => null);
  await page.waitForFunction(() => document.querySelectorAll('.market-news-panel .market-news-item').length >= 1, null, { timeout: 60000 }).catch(() => console.log('Market news items not found, continuing...'));
  await page.waitForTimeout(3000);
  // Click the news impact tab to make market news panel visible
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u65B0\u95FB\u5F71\u54CD')) { b.click(); return; } } });
  await page.waitForTimeout(2000);
  await page.evaluate(() => { const el = document.querySelector('.market-news-panel'); if (el) el.scrollIntoView({ behavior: 'instant', block: 'start' }); });
  await page.waitForTimeout(1000);
  await page.screenshot({ path: '../screenshots/sourcetag-market-panel-focus.png' });
  console.log('Market panel focused screenshot saved');
  await page.evaluate(() => { for (const b of document.querySelectorAll('.market-news-panel .market-news-button')) { if (b.textContent.includes('\u5C55\u5F00\u9605\u8BFB') && !b.disabled) { b.click(); return; } } });
  await page.waitForFunction(() => document.querySelector('.market-news-modal') !== null, null, { timeout: 10000 });
  await page.waitForTimeout(1000);
  await page.screenshot({ path: '../screenshots/sourcetag-modal-focus.png' });
  console.log('Modal focused screenshot saved');
  await browser.close();
};

run().catch(e => { console.error(e.message); process.exit(1); });
