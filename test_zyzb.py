import urllib.request
import json
url = 'https://emweb.securities.eastmoney.com/PC_HSF10/NewFinanceAnalysis/ZYZBAjaxNew?type=0&code=SH601899'
request = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0'})
response = urllib.request.urlopen(request)
text = response.read().decode('utf-8', errors='ignore')
print(text[:500])
data = json.loads(text)
print(data.get('data', [])[:1])
