import { chromium } from 'playwright';

const run = async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true }).catch(() => chromium.launch({ headless: true }));
  const page = await (await browser.newContext({ viewport: { width: 1600, height: 1000 } })).newPage();
  
  // Navigate and search stock
  await page.goto('http://localhost:5119', { waitUntil: 'networkidle' });
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u80A1\u7968\u4FE1\u606F')) { b.click(); return; } } });
  await page.waitForTimeout(1500);
  const symbolInput = page.locator('input[placeholder]').first();
  await symbolInput.fill('600000');
  await symbolInput.press('Enter');
  await page.waitForResponse(r => r.url().includes('/api/stocks/detail'), { timeout: 120000 }).catch(() => null);
  await page.waitForTimeout(5000);
  
  // Click news impact tab
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u65B0\u95FB\u5F71\u54CD')) { b.click(); return; } } });
  await page.waitForTimeout(2000);
  
  // Open modal
  await page.evaluate(() => { for (const b of document.querySelectorAll('.market-news-panel .market-news-button')) { if (b.textContent.includes('\u5C55\u5F00\u9605\u8BFB') && !b.disabled) { b.click(); return; } } });
  await page.waitForFunction(() => document.querySelector('.market-news-modal') !== null, null, { timeout: 10000 }).catch(() => {});
  await page.waitForTimeout(1000);
  
  // Scroll modal to bottom to see all items
  await page.evaluate(() => { const modal = document.querySelector('.market-news-modal'); if (modal) modal.scrollTop = modal.scrollHeight; });
  await page.waitForTimeout(500);
  await page.screenshot({ path: '../screenshots/news-modal-bottom.png' });
  console.log('Modal bottom screenshot saved');
  
  // Extract all visible source tags and sentiments from modal
  const modalData = await page.evaluate(() => {
    const items = document.querySelectorAll('.market-news-modal .market-news-item');
    return Array.from(items).map(item => {
      const tag = item.querySelector('.source-badge, [class*=source]')?.textContent?.trim() || 'no-tag';
      const title = item.querySelector('h4, h3, .title, [class*=title]')?.textContent?.trim() || item.textContent?.substring(0, 60) || '';
      const sentiment = item.querySelector('[class*=sentiment], .market-news-sentiment')?.textContent?.trim() || '';
      return { tag, title: title.substring(0, 80), sentiment };
    });
  });
  console.log('Modal items count: ' + modalData.length);
  modalData.forEach((d, i) => console.log((i+1) + '. [' + d.sentiment + '] [' + d.tag + '] ' + d.title));
  
  // Close modal
  await page.evaluate(() => { const btn = document.querySelector('.market-news-modal button'); if (btn && btn.textContent.includes('\u5173\u95ED')) btn.click(); });
  await page.waitForTimeout(500);
  
  // Navigate to 全量资讯库
  await page.evaluate(() => { for (const a of document.querySelectorAll('a, button')) { if (a.textContent.includes('\u5168\u91CF\u8D44\u8BAF\u5E93')) { a.click(); return; } } });
  await page.waitForTimeout(3000);
  await page.screenshot({ path: '../screenshots/news-archive-page.png' });
  console.log('Archive page screenshot saved');
  
  // Check archive content
  const archiveData = await page.evaluate(() => {
    const rows = document.querySelectorAll('table tr, .news-row, [class*=archive] [class*=item], .news-list .news-item');
    return { rowCount: rows.length, bodyText: document.body.textContent.substring(0, 500) };
  });
  console.log('Archive rows: ' + archiveData.rowCount);
  
  await browser.close();
};

run().catch(e => { console.error(e.message); process.exit(1); });
