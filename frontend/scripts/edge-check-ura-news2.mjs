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
  await page.waitForTimeout(5000);
  
  // Click news impact tab
  await page.evaluate(() => { for (const b of document.querySelectorAll('button')) { if (b.textContent.includes('\u65B0\u95FB\u5F71\u54CD')) { b.click(); return; } } });
  await page.waitForTimeout(2000);
  
  // Check the right panel: count visible items in the sidebar preview
  const sidebarInfo = await page.evaluate(() => {
    const panel = document.querySelector('.market-news-panel');
    if (!panel) return { found: false };
    const items = panel.querySelectorAll('.market-news-item');
    const badges = panel.querySelectorAll('[class*=source-badge], [class*=sourceTag]');
    const expandBtn = Array.from(panel.querySelectorAll('button')).find(b => b.textContent.includes('\u5C55\u5F00\u9605\u8BFB'));
    return {
      found: true,
      visibleItemsCount: items.length,
      badgesCount: badges.length,
      hasExpandButton: !!expandBtn,
      panelHeight: panel.offsetHeight,
      panelScrollHeight: panel.scrollHeight
    };
  });
  console.log('Sidebar panel info:', JSON.stringify(sidebarInfo));
  
  // Scroll the right panel to see all items
  await page.evaluate(() => { 
    const panel = document.querySelector('.market-news-panel');
    if (panel) panel.scrollTop = panel.scrollHeight;
  });
  await page.waitForTimeout(500);
  await page.screenshot({ path: '../screenshots/news-sidebar-scrolled.png' });
  console.log('Sidebar scrolled screenshot saved');
  
  // Navigate to archive
  await page.evaluate(() => { for (const a of document.querySelectorAll('a, button')) { if (a.textContent.includes('\u5168\u91CF\u8D44\u8BAF\u5E93')) { a.click(); return; } } });
  await page.waitForTimeout(3000);
  
  // Count archive items
  const archiveInfo = await page.evaluate(() => {
    const totalText = document.body.textContent.match(/共\s*(\d+)\s*条资讯/);
    const pageText = document.body.textContent.match(/(\d+)\s*\/\s*(\d+)\s*页/);
    const items = document.querySelectorAll('.news-card, [class*=fact-card], [class*=news-item]');
    const sourceBadges = document.querySelectorAll('[class*=source-badge], [class*=sourceTag]');
    return {
      totalText: totalText ? totalText[0] : 'not found',
      pageText: pageText ? pageText[0] : 'not found',
      visibleCards: items.length,
      sourceBadges: sourceBadges.length
    };
  });
  console.log('Archive info:', JSON.stringify(archiveInfo));
  
  // Scroll archive down
  await page.evaluate(() => window.scrollTo(0, document.body.scrollHeight));
  await page.waitForTimeout(500);
  await page.screenshot({ path: '../screenshots/news-archive-bottom.png' });
  console.log('Archive bottom screenshot saved');
  
  // Try filtering by source
  const filterOptions = await page.evaluate(() => {
    const selects = document.querySelectorAll('select');
    const allOptions = [];
    selects.forEach(sel => {
      const opts = Array.from(sel.options).map(o => o.textContent + ':' + o.value);
      allOptions.push({ selectName: sel.name || sel.id || 'unknown', options: opts });
    });
    return allOptions;
  });
  console.log('Filter options:', JSON.stringify(filterOptions));
  
  await browser.close();
};

run().catch(e => { console.error(e.message); process.exit(1); });
