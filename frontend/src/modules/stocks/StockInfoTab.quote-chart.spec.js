import { beforeEach, describe, expect, it, vi } from 'vitest'

import { stockInfoTabQuoteChartCases } from './StockInfoTab.quote-chart.cases'
import { installStockInfoTabCaseSuite } from './stockInfoTabTestUtils'

installStockInfoTabCaseSuite({ beforeEach, describe, expect, it, vi }, stockInfoTabQuoteChartCases)