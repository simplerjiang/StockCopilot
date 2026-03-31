import { chromium } from 'playwright';

const baseUrl = 'http://localhost:5119';

const run = async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true }).catch(async () => {
    return chromium.launch({ headless: true });
  });

  const context = await browser.newContext({ viewport: { width: 1600, height: 1000 } });
  const page = await context.newPage();

  const consoleErrors = [];
  page.on('console', msg => {
    if (msg.type() === 'error') consoleErrors.push(msg.text());
  });

  await page.goto(baseUrl, { waitUntil: 'networkidle' });

  // Click stock info tab using unicode escape
  await page.evaluate(() => {
    const btns = document.querySelectorAll('button');
    for (const b of btns) {
      if (b.textContent.includes('\u80A1\u7968\u4FE1\u606F')) { b.click(); return; }
    }
  });

  // Wait for market news API
  const marketNewsReady = page.waitForResponse(
    resp => resp.url().includes('/api/news') && resp.url().includes('level=market') && resp.status() === 200,
    { timeout: 30000 }
  ).catch(() => null);

  // Enter stock symbol
  await page.waitForTimeout(1500);
  const symbolInput = page.locator('input[placeholder]').first();
  await symbolInput.fill('600000');
  await symbolInput.press('Enter');

  // Wait for detail load
  await page.waitForResponse(resp => resp.url().includes('/api/stocks/detail') && resp.status() === 200, { timeout: 90000 });
  await marketNewsReady;

  // Wait for market news items to appear in DOM
  await page.waitForFunction(
    () => document.querySelectorAll('.market-news-panel .market-news-item').length >= 1,
    null,
    { timeout: 30000 }
  );
  await page.waitForTimeout(3000);

  // ---- CHECK 1: Preview count ----
  const previewCount = await page.evaluate(
    () => document.querySelectorAll('.market-news-preview-list > .market-news-item').length
  );
  console.log('[CHECK 1] Preview count: ' + previewCount + ' (expected <=5)');

  // ---- CHECK 2: sourceTag badges in preview ----
  const previewBadges = await page.evaluate(() => {
    const badges = document.querySelectorAll('.market-news-preview-list .source-tag-badge');
    return {
      count: badges.length,
      texts: Array.from(badges).map(b => b.textContent.trim())
    };
  });
  console.log('[CHECK 2] Preview sourceTag badges: ' + previewBadges.count);
  console.log('[CHECK 2] Badge texts: ' + JSON.stringify(previewBadges.texts));

  // ---- CHECK 3: Badge computed styles ----
  const badgeStyles = await page.evaluate(() => {
    const badge = document.querySelector('.market-news-preview-list .source-tag-badge');
    if (!badge) return null;
    const s = getComputedStyle(badge);
    return {
      borderRadius: s.borderRadius,
      color: s.color,
      backgroundColor: s.backgroundColor,
      fontSize: s.fontSize,
      display: s.display,
      border: s.border,
      padding: s.padding
    };
  });
  console.log('[CHECK 3] Badge styles: ' + JSON.stringify(badgeStyles));

  // ---- CHECK 4: Card gap ----
  const cardGap = await page.evaluate(() => {
    const list = document.querySelector('.market-news-preview-list');
    if (!list) return null;
    const s = getComputedStyle(list);
    return { gap: s.gap, rowGap: s.rowGap, columnGap: s.columnGap };
  });
  console.log('[CHECK 4] Card gap: ' + JSON.stringify(cardGap));

  // ---- CHECK 4b: Actual card spacing ----
  const cardSpacing = await page.evaluate(() => {
    const items = document.querySelectorAll('.market-news-preview-list > .market-news-item');
    if (items.length < 2) return null;
    const r0 = items[0].getBoundingClientRect();
    const r1 = items[1].getBoundingClientRect();
    return { spacingPx: Math.round(r1.top - r0.bottom) };
  });
  console.log('[CHECK 4b] Actual card spacing px: ' + JSON.stringify(cardSpacing));

  // ---- Screenshot: Preview area ----
  try {
    const panelEl = page.locator('.market-news-panel').first();
    await panelEl.scrollIntoViewIfNeeded();
    await panelEl.screenshot({ path: 'screenshots/sourcetag-preview.png' });
    console.log('[SCREENSHOT] Preview => screenshots/sourcetag-preview.png');
  } catch {
    await page.screenshot({ path: 'screenshots/sourcetag-preview-full.png', fullPage: true });
    console.log('[SCREENSHOT] Fullpage fallback => screenshots/sourcetag-preview-full.png');
  }

  // ---- CHECK 5: Modal ----
  const hasExpandBtn = await page.evaluate(() => {
    const btns = document.querySelectorAll('.market-news-panel .market-news-button');
    for (const b of btns) {
      if (b.textContent.includes('\u5C55\u5F00\u9605\u8BFB') && !b.disabled) return true;
    }
    return false;
  });

  if (hasExpandBtn) {
    await page.evaluate(() => {
      const btns = document.querySelectorAll('.market-news-panel .market-news-button');
      for (const b of btns) {
        if (b.textContent.includes('\u5C55\u5F00\u9605\u8BFB') && !b.disabled) { b.click(); return; }
      }
    });
    await page.waitForFunction(
      () => document.querySelector('.market-news-modal') !== null,
      null,
      { timeout: 10000 }
    );
    await page.waitForTimeout(1000);

    const modalInfo = await page.evaluate(() => {
      const modal = document.querySelector('.market-news-modal');
      if (!modal) return null;
      const items = modal.querySelectorAll('.market-news-item');
      const badges = modal.querySelectorAll('.source-tag-badge');
      return {
        itemCount: items.length,
        badgeCount: badges.length,
        badgeTexts: Array.from(badges).slice(0, 10).map(b => b.textContent.trim())
      };
    });
    console.log('[CHECK 5] Modal items: ' + (modalInfo ? modalInfo.itemCount : 0));
    console.log('[CHECK 5] Modal sourceTag badges: ' + (modalInfo ? modalInfo.badgeCount : 0));
    console.log('[CHECK 5] Modal badge texts: ' + JSON.stringify(modalInfo ? modalInfo.badgeTexts : []));

    try {
      const modalEl = page.locator('.market-news-modal').first();
      await modalEl.screenshot({ path: 'screenshots/sourcetag-modal.png' });
      console.log('[SCREENSHOT] Modal => screenshots/sourcetag-modal.png');
    } catch {
      await page.screenshot({ path: 'screenshots/sourcetag-modal-full.png' });
      console.log('[SCREENSHOT] Modal fullpage => screenshots/sourcetag-modal-full.png');
    }

    // Close modal
    await page.evaluate(() => {
      const btns = document.querySelectorAll('.market-news-modal .market-news-button');
      for (const b of btns) {
        if (b.textContent.includes('\u5173\u95ED')) { b.click(); return; }
      }
    });
    await page.waitForTimeout(500);
  } else {
    console.log('[CHECK 5] Expand button disabled or not found, skipping modal');
  }

  // ---- CHECK 6: Archive ----
  const hasArchive = await page.evaluate(() => {
    const btns = document.querySelectorAll('button');
    for (const b of btns) {
      if (b.textContent.includes('\u5168\u91CF\u8D44\u8BAF\u5E93')) return true;
    }
    return false;
  });

  if (hasArchive) {
    const archiveResp = page.waitForResponse(
      resp => resp.url().includes('/api/news/archive') && resp.status() === 200,
      { timeout: 90000 }
    );
    await page.evaluate(() => {
      const btns = document.querySelectorAll('button');
      for (const b of btns) {
        if (b.textContent.includes('\u5168\u91CF\u8D44\u8BAF\u5E93')) { b.click(); return; }
      }
    });
    await archiveResp;
    await page.waitForFunction(
      () => document.querySelectorAll('.archive-card').length >= 1,
      null,
      { timeout: 20000 }
    );
    await page.waitForTimeout(2000);

    const archiveInfo = await page.evaluate(() => {
      const cards = document.querySelectorAll('.archive-card');
      const stBadges = document.querySelectorAll('.archive-card .source-tag-badge');
      const allBadges = document.querySelectorAll('.archive-card .archive-badge');

      // Check badge coordination - get all badge types and their colors
      const badgeTypes = {};
      document.querySelectorAll('.archive-card .archive-badge').forEach(b => {
        const cls = Array.from(b.classList).find(c => c !== 'archive-badge');
        if (cls && !badgeTypes[cls]) {
          const s = getComputedStyle(b);
          badgeTypes[cls] = { bg: s.backgroundColor, color: s.color, border: s.border };
        }
      });

      return {
        cardCount: cards.length,
        sourceTagCount: stBadges.length,
        sourceTagTexts: Array.from(stBadges).slice(0, 8).map(b => b.textContent.trim()),
        totalBadges: allBadges.length,
        badgeTypeStyles: badgeTypes
      };
    });
    console.log('[CHECK 6] Archive cards: ' + archiveInfo.cardCount);
    console.log('[CHECK 6] Archive sourceTag badges: ' + archiveInfo.sourceTagCount);
    console.log('[CHECK 6] Archive sourceTag texts: ' + JSON.stringify(archiveInfo.sourceTagTexts));
    console.log('[CHECK 6] Archive total badges: ' + archiveInfo.totalBadges);
    console.log('[CHECK 6] Badge type styles: ' + JSON.stringify(archiveInfo.badgeTypeStyles));

    await page.screenshot({ path: 'screenshots/sourcetag-archive.png', fullPage: false });
    console.log('[SCREENSHOT] Archive => screenshots/sourcetag-archive.png');
  } else {
    console.log('[CHECK 6] Archive button not found');
  }

  // ---- ERRORS ----
  console.log('\n[ERRORS] Console errors: ' + consoleErrors.length);
  if (consoleErrors.length) {
    consoleErrors.slice(0, 5).forEach(e => console.log('  - ' + e.substring(0, 200)));
  }

  await browser.close();

  // ---- SUMMARY ----
  console.log('\n========== UI REVIEW SUMMARY ==========');
  console.log('Preview items: ' + previewCount);
  console.log('Preview sourceTag badges: ' + previewBadges.count);
  console.log('Card gap: ' + (cardGap ? cardGap.gap : 'N/A'));
  console.log('Badge style pill: ' + (badgeStyles ? badgeStyles.borderRadius : 'N/A'));
  console.log('Badge color: ' + (badgeStyles ? badgeStyles.color : 'N/A'));
  console.log('Badge bg: ' + (badgeStyles ? badgeStyles.backgroundColor : 'N/A'));
  console.log('Console errors: ' + consoleErrors.length);
  console.log('========================================');
};

run().catch(err => {
  console.error('UI review FAILED:', err.message);
  process.exit(1);
});
