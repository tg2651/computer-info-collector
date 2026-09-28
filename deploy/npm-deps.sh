#!/bin/bash
exec > /root/npm-deps.log 2>&1
set -x
cd /opt/collector
export PATH=/usr/local/bin:$PATH

npm install --omit=dev --no-fund --no-audit \
  --registry=https://registry.npmmirror.com \
  --better_sqlite3_binary_host=https://registry.npmmirror.com/-/binary/better-sqlite3

node -e "require('express');const D=require('better-sqlite3');const x=new D(':memory:');x.exec('create table t(a)');x.prepare('insert into t values (?)').run(1);console.log('SQLITE_OK', x.prepare('select a from t').get());require('exceljs');console.log('MODULES_OK')"

systemctl restart collector
sleep 3
systemctl is-active collector
ss -ltnp | grep ':3000' || true
curl -s -o /dev/null -w 'app   127.0.0.1:3000 -> HTTP %{http_code}\n' http://127.0.0.1:3000/api/records
curl -s -o /dev/null -w 'nginx 127.0.0.1:80   -> HTTP %{http_code}\n' http://127.0.0.1/api/records
echo DEPS_DONE
