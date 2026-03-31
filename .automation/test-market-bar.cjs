const { chromium } = require('playwright');
(async () => {
  console.log('Launching browser...');
  const browser = await chromium.launch({ channel: 'msedge', headless: false, args: ['--no-sandbox'] });
  const ctx = await browser.newContext({ viewport: { width: 1600, height: 900 } });
  const page = await ctx.newPage();
  
  console.log('Navigating...');
  await page.goto('http://localhost:5119/?tab=stock-info', { waitUntil: 'domcontentloaded', timeout: 30000 });
  console.log('DOM loaded. Waiting 8s for data...');
  await page.waitForTimeout(8000);
  
  // Screenshot
  await page.screenshot({ path: 'C:/Users/kong/AiAgent/.automation/market-bar-test.png' });
  console.log('Screenshot saved');

  // Counts
  const cards = await page.evaluate(() => document.querySelectorAll('.idx-card').length);
  const pulses = await page.evaluate(() => document.querySelectorAll('.pulse-chip').length);
  console.log('Index cards: ' + cards);
  console.log('Pulse chips: ' + pulses);

  // Names
  const names = await page.evaluate(() => Array.from(document.querySelectorAll('.idx-card-name')).map(e => e.textContent.trim()));
  console.log('Names: ' + JSON.stringify(names));

  // Pulse values
  const pvals = await page.evaluate(() => Array.from(document.querySelectorAll('.pulse-chip')).map(e => e.textContent.trim()));
  console.log('Pulses: ' + JSON.stringify(pvals));

  // Bar status
  const status = await page.evaluate(() => { const s = document.querySelector('.bar-status'); return s ? s.textContent.trim() : 'NOT FOUND'; });
  console.log('Status: ' + status);

  // Check if bar is showing or hidden
  const barClass = await page.evaluate(() => { const b = document.querySelector('.market-bar'); return b ? b.className : 'NO BAR'; });
  console.log('Bar class: ' + barClass);

  // Console errors
  const errors = [];
  page.on('console', msg => { if (msg.type() === 'error') errors.push(msg.text()); });
  await page.waitForTimeout(2000);
  if (errors.length) console.log('Console errors: ' + JSON.stringify(errors));
  else console.log('No console errors');

  await browser.close();
  console.log('DONE');
})().catch(e => { console.error('FATAL: ' + e.message); process.exit(1); });
