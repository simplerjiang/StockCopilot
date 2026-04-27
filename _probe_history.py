import sqlite3, os, sys
LOCAL = os.path.expandvars(r'%LOCALAPPDATA%\SimplerJiangAiAgent')
RAG = os.path.join(LOCAL, 'App_Data', 'financial-rag.db')
MAIN = os.path.join(LOCAL, 'data', 'SimplerJiangAiAgent.db')

def list_tables(path):
    c = sqlite3.connect(f'file:{path}?mode=ro', uri=True)
    cur = c.cursor()
    cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")
    rows = [r[0] for r in cur.fetchall()]
    c.close()
    return rows

print('=== financial-rag.db tables ===')
for t in list_tables(RAG): print(' ', t)
print()
print('=== SimplerJiangAiAgent.db tables ===')
for t in list_tables(MAIN): print(' ', t)
