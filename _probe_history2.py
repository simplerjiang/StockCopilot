import sqlite3, os
LOCAL = os.path.expandvars(r'%LOCALAPPDATA%\SimplerJiangAiAgent')
RAG = os.path.join(LOCAL, 'App_Data', 'financial-rag.db')
MAIN = os.path.join(LOCAL, 'data', 'SimplerJiangAiAgent.db')
SYMBOLS = ['sh600519','sz000001','bj430510']

def q(path, sql, params=()):
    c = sqlite3.connect(f'file:{path}?mode=ro', uri=True); cur=c.cursor()
    cur.execute(sql, params)
    rows = cur.fetchall()
    c.close()
    return rows

# Watchlist context
print('=== Watchlist active ===')
for r in q(MAIN, "SELECT Symbol, Name, IsEnabled, CreatedAt FROM ActiveWatchlists ORDER BY Symbol LIMIT 30"):
    print(' ', r)

print('\n=== KLine totals & ranges per symbol ===')
for s in SYMBOLS:
    rows = q(MAIN, "SELECT Interval, COUNT(*), MIN(Date), MAX(Date) FROM KLinePoints WHERE Symbol=? GROUP BY Interval", (s,))
    print(f' {s}:')
    if not rows:
        print('   (no rows)')
    for r in rows:
        print('  ', r)

print('\n=== News totals & earliest/latest per symbol ===')
for s in SYMBOLS:
    rows = q(MAIN, "SELECT COUNT(*), MIN(PublishTime), MAX(PublishTime) FROM LocalStockNews WHERE Symbol=?", (s,))
    print(f' {s}: {rows[0]}')

print('\n=== Quote snapshots earliest/latest per symbol ===')
for s in SYMBOLS:
    rows = q(MAIN, "SELECT COUNT(*), MIN(Timestamp), MAX(Timestamp) FROM StockQuoteSnapshots WHERE Symbol=?", (s,))
    print(f' {s}: {rows[0]}')

print('\n=== RAG chunks per symbol (PDF coverage) ===')
for s in SYMBOLS:
    # Symbol may be stored without prefix
    rows = q(RAG, "SELECT COUNT(*), MIN(report_date), MAX(report_date), MIN(created_at), MAX(created_at) FROM chunks WHERE symbol=? OR symbol=? OR symbol=?",
             (s, s.replace('sh','').replace('sz','').replace('bj',''), s.upper()))
    print(f' {s}: {rows[0]}')

# Aggregate context
print('\n=== Total LocalStockNews oldest 5 + newest 5 ===')
for r in q(MAIN, "SELECT MIN(PublishTime), MAX(PublishTime), COUNT(*) FROM LocalStockNews"):
    print(' aggregate:', r)

print('\n=== KLine intervals overall ===')
for r in q(MAIN, "SELECT Interval, COUNT(*), MIN(Date), MAX(Date) FROM KLinePoints GROUP BY Interval"):
    print(' ', r)

print('\n=== RAG distinct symbols sample ===')
for r in q(RAG, "SELECT symbol, COUNT(*) FROM chunks GROUP BY symbol ORDER BY COUNT(*) DESC LIMIT 10"):
    print(' ', r)
