import { beforeEach, describe, expect, it, vi } from 'vitest'

import { stockInfoTabSwitchingCases } from './StockInfoTab.switching.cases'
import { installStockInfoTabCaseSuite } from './stockInfoTabTestUtils'

installStockInfoTabCaseSuite({ beforeEach, describe, expect, it, vi }, stockInfoTabSwitchingCases)