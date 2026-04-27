import sqlite3, os
LOCAL = os.path.expandvars(r'%LOCALAPPDATA%\SimplerJiangAiAgent')
RAG = os.path.join(LOCAL, 'App_Data', 'financial-rag.db')
MAIN = os.path.join(LOCAL, 'data', 'SimplerJiangAiAgent.db')
SYMBOLS = ['sh600519','sz000001','bj430510']

def schema(path, table):
    c = sqlite3.connect(f'file:{path}?mode=ro', uri=True); cur=c.cursor()
    cur.execute(f"PRAGMA table_info({table})")
    cols = [(r[1], r[2]) for r in cur.fetchall()]
    c.close()
    return cols

print('--- KLinePoints ---'); print(schema(MAIN,'KLinePoints'))
print('--- LocalStockNews ---'); print(schema(MAIN,'LocalStockNews'))
print('--- StockQuoteSnapshots ---'); print(schema(MAIN,'StockQuoteSnapshots'))
print('--- StockCompanyProfiles ---'); print(schema(MAIN,'StockCompanyProfiles'))
print('--- chunks (RAG) ---'); print(schema(RAG,'chunks'))
print('--- ActiveWatchlists ---'); print(schema(MAIN,'ActiveWatchlists'))
