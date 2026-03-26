const http = require('http');
const fs = require('fs');

const req = http.request('http://localhost:5119/api/stocks/mcp/kline?symbol=sh601899', (res) => {
  let data = '';
  res.on('data', (chunk) => {
    data += chunk;
  });
  res.on('end', () => {
    fs.writeFileSync('d:\\SimplerJiangAiAgent\\.automation\\tmp\\kline-res2.txt', 'Status: ' + res.statusCode + '\n\n' + data);
    console.log('done!');
  });
});

req.on('error', (e) => {
  fs.writeFileSync('d:\\SimplerJiangAiAgent\\.automation\\tmp\\kline-res2.txt', 'Error: ' + e.message);
  console.log('error!', e);
});
req.end();
