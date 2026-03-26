import os
import glob

files = glob.glob('noupload/TradingAgents-main/tradingagents/agents/**/*.py', recursive=True)
prompts = []

for f in files:
    try:
        content = open(f, encoding='utf-8').read()
        if 'system_message' in content or 'ChatPromptTemplate' in content or 'prompt' in content.lower():
            prompts.append(f"\n\n======= {f} =======\n")
            lines = content.split('\n')
            for i, line in enumerate(lines):
                if 'system_message' in line or 'prompt' in line.lower() or '"""' in line or "'''" in line:
                    prompts.append(f"Line {i+1}: {line}")
    except:
        pass

with open('prompts_dump.txt', 'w', encoding='utf-8') as out:
    out.write('\n'.join(prompts))
