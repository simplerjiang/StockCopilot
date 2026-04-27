import sqlite3, os
LOCAL = os.path.expandvars(r'%LOCALAPPDATA%\SimplerJiangAiAgent')
MAIN = os.path.join(LOCAL, 'data', 'SimplerJiangAiAgent.db')

def q(sql, params=()):
    c = sqlite3.connect(f'file:{MAIN}?mode=ro', uri=True); cur=c.cursor()
    cur.execute(sql, params); rows = cur.fetchall(); c.close(); return rows

print('--- KLine distinct symbols ---')
for r in q("SELECT Symbol, COUNT(*), MIN(Date), MAX(Date) FROM KLinePoints GROUP BY Symbol ORDER BY 2 DESC"):
    print(' ', r)

print('\n--- News distinct symbols (top 15) ---')
for r in q("SELECT Symbol, COUNT(*), MIN(PublishTime), MAX(PublishTime) FROM LocalStockNews WHERE Symbol IS NOT NULL AND Symbol<>'' GROUP BY Symbol ORDER BY 2 DESC LIMIT 15"):
    print(' ', r)

print('\n--- A BJ-prefixed symbol if any ---')
for r in q("SELECT Symbol, COUNT(*) FROM KLinePoints WHERE Symbol LIKE 'bj%' OR Symbol LIKE 'BJ%' GROUP BY Symbol"):
    print(' KLine bj:', r)

# Check for any minute lines / intraday
print('\n--- MinuteLinePoints ---')
for r in q("SELECT COUNT(*), MIN(rowid), MAX(rowid) FROM MinuteLinePoints"):
    print(' ', r)

# Any sentiment / index history
print('\n--- MarketIndexSnapshots overall ---')
for r in q("SELECT COUNT(*), MIN(rowid) FROM MarketIndexSnapshots"):
    print(' ', r)

# Recommendation/research session age
print('\n--- ResearchSessions age ---')
for r in q("PRAGMA table_info(ResearchSessions)"):
    if r[1] in ('CreatedAt','StartedAt','UpdatedAt'):
        print(' col:', r)
for r in q("SELECT COUNT(*), MIN(StartedAt), MAX(StartedAt) FROM ResearchSessions"):
    print(' ', r)

# KLine for explicit prefixes
print('\n--- Verify if sh600519 KLine missing ---')
for prefix in ['sh600519','SH600519','600519','sz000001','SZ000001','000001','bj430510','BJ430510','430510']:
    for r in q("SELECT COUNT(*), MIN(Date), MAX(Date) FROM KLinePoints WHERE Symbol=?", (prefix,)):
        print(f'  {prefix}: {r}')
