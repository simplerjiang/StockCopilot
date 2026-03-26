import http from 'http';

const req = http.request('http://localhost:5119/api/stocks/mcp/kline?symbol=sh601899', (res) => {
  let data = '';
  res.on('data', (chunk) => {
    data += chunk;
  });
  res.on('end', () => {
    console.log(`Status: ${res.statusCode}`);
    console.log(JSON.stringify(JSON.parse(data), null, 2).slice(0, 1000));
  });
});

req.on('error', (e) => {
  console.error(`Problem with request: ${e.message}`);
});
req.end();
