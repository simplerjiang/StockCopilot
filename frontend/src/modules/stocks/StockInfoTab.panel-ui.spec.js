import { beforeEach, describe, expect, it, vi } from 'vitest'

import { stockInfoTabPanelUiCases } from './StockInfoTab.panel-ui.cases'
import { installStockInfoTabCaseSuite } from './stockInfoTabTestUtils'

installStockInfoTabCaseSuite({ beforeEach, describe, expect, it, vi }, stockInfoTabPanelUiCases)