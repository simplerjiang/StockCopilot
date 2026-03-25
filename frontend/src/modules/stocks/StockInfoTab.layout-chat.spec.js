import { beforeEach, describe, expect, it, vi } from 'vitest'

import { stockInfoTabLayoutChatCases } from './StockInfoTab.layout-chat.cases'
import { installStockInfoTabCaseSuite } from './stockInfoTabTestUtils'

installStockInfoTabCaseSuite({ beforeEach, describe, expect, it, vi }, stockInfoTabLayoutChatCases)