import { chromium } from 'playwright'
import fs from 'node:fs'
import path from 'node:path'

const baseUrl = process.env.UI_BASE_URL || 'http://localhost:5119'
const outputDir = path.resolve('docs', 'screenshots')

fs.mkdirSync(outputDir, { recursive: true })

async function launchBrowser() {
  try {
    return await chromium.launch({
      channel: 'msedge',
      headless: true
    })
  } catch {
    return chromium.launch({ headless: true })
  }
}

async function waitForAppShell(page) {
  await page.goto(baseUrl, { waitUntil: 'domcontentloaded', timeout: 45000 })
  await page.waitForLoadState('networkidle', { timeout: 45000 }).catch(() => {})
  await page.getByText('SimplerJiang AI Agent').waitFor({ timeout: 15000 })
}

async function capture(page, fileName) {
  await page.screenshot({
    path: path.join(outputDir, fileName)
  })
}

async function queryStock(page, symbol) {
  const symbolInput = page.getByPlaceholder('输入股票代码/名称/拼音缩写')
  await symbolInput.fill(symbol)
  await page.getByRole('button', { name: '查询' }).click()
  await page.waitForTimeout(8000)
}

async function clickTextButton(page, text) {
  await page.getByRole('button', { name: text, exact: true }).click()
  await page.waitForTimeout(2500)
}

async function scrollChartIntoHeroFrame(page) {
  const chartAnchor = page.locator('.chart-mode').first()
  await chartAnchor.scrollIntoViewIfNeeded()
  await page.mouse.wheel(0, 280)
  await page.waitForTimeout(1200)
}

async function captureAgentAnalysis(page) {
  const panelHeader = page.getByText('多Agent分析')
  await panelHeader.scrollIntoViewIfNeeded()
  await page.waitForTimeout(1200)
  await page.locator('.run-standard-button').click()

  const summary = page.locator('.agent-card .summary').first()
  await summary.waitFor({ timeout: 120000 })
  await page.waitForTimeout(1500)
}

const browser = await launchBrowser()

try {
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1
  })
  const page = await context.newPage()

  await waitForAppShell(page)

  await queryStock(page, 'sh600000')
  await capture(page, 'stock-terminal-1920x1080.png')

  await scrollChartIntoHeroFrame(page)
  await clickTextButton(page, '日K图')
  await capture(page, 'stock-dayk-1920x1080.png')

  await clickTextButton(page, '月K图')
  await capture(page, 'stock-monthk-1920x1080.png')

  await captureAgentAnalysis(page)
  await capture(page, 'multi-agent-analysis-1920x1080.png')

  await page.getByRole('button', { name: '情绪轮动' }).click()
  await page.waitForTimeout(3000)
  await capture(page, 'market-sentiment-1920x1080.png')

  await page.goto(`${baseUrl}/?tab=admin-llm`, { waitUntil: 'domcontentloaded', timeout: 45000 })
  await page.waitForLoadState('networkidle', { timeout: 45000 }).catch(() => {})
  await page.getByText('管理员登录').waitFor({ timeout: 15000 })
  await capture(page, 'llm-onboarding-1920x1080.png')

  await context.close()
} finally {
  await browser.close()
}