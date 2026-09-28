#!/bin/bash
exec > /root/fix-build2.log 2>&1
set -x
source /opt/rh/devtoolset-11/enable
export PATH=/usr/local/bin:$PATH
export npm_config_registry=https://registry.npmmirror.com
export npm_config_disturl=https://registry.npmmirror.com/-/binary/node
export LD_LIBRARY_PATH=/opt/rh/devtoolset-11/root/usr/lib64

GYP=/usr/local/lib/node_modules/npm/node_modules/node-gyp/bin/node-gyp.js
ls -la "$GYP"
gcc --version | head -1

cd /opt/collector/node_modules/better-sqlite3
rm -rf build
node "$GYP" rebuild --release --python=/usr/bin/python3

ls -la build/Release/
cp -f build/Release/better_sqlite3.node prebuilds/linux-x64.node
echo '-- compiled binary glibc requirements --'
objdump -T prebuilds/linux-x64.node | grep -oE 'GLIBC_[0-9.]+' | sort -uV | tail -3

cd /opt/collector
node -e "const D=require('better-sqlite3');const x=new D(':memory:');x.exec('create table t(a)');x.prepare('insert into t values (?)').run(42);console.log('SQLITE_OK', x.prepare('select a from t').get());require('express');require('exceljs');console.log('MODULES_OK')"

systemctl restart collector
sleep 3
systemctl is-active collector
ss -ltnp | grep ':3000' || true
curl -s -o /dev/null -w 'app   127.0.0.1:3000 -> HTTP %{http_code}\n' http://127.0.0.1:3000/api/records
curl -s -o /dev/null -w 'nginx 127.0.0.1:80   -> HTTP %{http_code}\n' http://127.0.0.1/api/records
echo BUILD2_DONE
