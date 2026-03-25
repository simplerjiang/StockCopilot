import { beforeEach, describe, expect, it, vi } from 'vitest'

import { stockInfoTabNewsHistoryCases } from './StockInfoTab.news-history.cases'
import { installStockInfoTabCaseSuite } from './stockInfoTabTestUtils'

installStockInfoTabCaseSuite({ beforeEach, describe, expect, it, vi }, stockInfoTabNewsHistoryCases)