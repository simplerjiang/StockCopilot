import { chromium } from 'playwright';

const baseUrl = 'http://localhost:5119';
const userDataDir = '../.automation/edge-profile';

const context = await chromium.launchPersistentContext(userDataDir, {
  channel: 'msedge',
  headless: true,
  viewport: { width: 1440, height: 900 }
});

try {
  const page = context.pages()[0] ?? await context.newPage();
  await page.goto(`${baseUrl}/?tab=stock-info`, { waitUntil: 'domcontentloaded', timeout: 30000 });
  await page.waitForTimeout(5000);
  
  // Get the page HTML to understand structure
  const bodyText = await page.evaluate(() => document.body.innerText.substring(0, 2000));
  console.log('=== PAGE TEXT (first 2000 chars) ===');
  console.log(bodyText);
  
  console.log('\n=== MARKET BAR ELEMENTS ===');
  const marketBarCount = await page.locator('.market-bar').count();
  console.log('market-bar count:', marketBarCount);
  
  const idxCardCount = await page.locator('.idx-card').count();
  console.log('idx-card count:', idxCardCount);
  
  // Try broader selectors
  const articleCount = await page.locator('article').count();
  console.log('article count:', articleCount);
  
  // Check if maybe the market bar is hidden
  const hiddenBar = await page.locator('.market-bar-hidden').count();
  console.log('market-bar-hidden count:', hiddenBar);
  
  // Check for the "显示" button
  const showBtn = await page.locator('button:has-text("显示")').count();
  console.log('显示 button count:', showBtn);
  
  // Check which tab is active
  const tabs = await page.locator('[role="tab"], .tab-item, .nav-item, button').allTextContents();
  console.log('Buttons found:', tabs.filter(t => t.trim()).slice(0, 20).join(' | '));

  // Check URL
  console.log('Current URL:', page.url());

  await page.screenshot({ path: '../.automation/reports/market-bar-chart-fix/debug-page.png', fullPage: true });
  console.log('Screenshot saved');
} finally {
  await context.close();
}
