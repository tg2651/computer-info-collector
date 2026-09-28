#!/bin/bash
exec > /root/fix-build.log 2>&1
set -x

echo "===== [1] repos -> aliyun (base 7.9.2009 / SCLo / EPEL archive) ====="
mkdir -p /root/repo-bak
mv /etc/yum.repos.d/CentOS-*.repo /root/repo-bak/ 2>/dev/null || true

cat > /etc/yum.repos.d/CentOS-Base.repo <<'EOF'
[base]
name=CentOS-7.9.2009 - Base - aliyun
baseurl=http://mirrors.aliyun.com/centos/7.9.2009/os/$basearch/
gpgcheck=0
enabled=1

[updates]
name=CentOS-7.9.2009 - Updates - aliyun
baseurl=http://mirrors.aliyun.com/centos/7.9.2009/updates/$basearch/
gpgcheck=0
enabled=1

[extras]
name=CentOS-7.9.2009 - Extras - aliyun
baseurl=http://mirrors.aliyun.com/centos/7.9.2009/extras/$basearch/
gpgcheck=0
enabled=1
EOF

cat > /etc/yum.repos.d/CentOS-SCLo.repo <<'EOF'
[centos-sclo-rh]
name=SCLo - rh - aliyun
baseurl=http://mirrors.aliyun.com/centos/7/sclo/$basearch/rh/
gpgcheck=0
enabled=1
EOF

cat > /etc/yum.repos.d/epel.repo <<'EOF'
[epel]
name=EPEL-7 archive - aliyun
baseurl=https://mirrors.aliyun.com/epel-archive/7/$basearch/
gpgcheck=0
enabled=1
EOF

yum clean all
yum makecache -y

echo "===== [2] toolchain ====="
DTS=""
yum install -y make python3 || true
if yum install -y devtoolset-12-gcc-c++; then DTS=12;
elif yum install -y devtoolset-11-gcc-c++; then DTS=11;
else echo NO_DEVTOOLSET; exit 1; fi
echo "using devtoolset-$DTS"
source /opt/rh/devtoolset-$DTS/enable
gcc --version | head -1
python3 --version

echo "===== [3] compile better-sqlite3 from source ====="
cd /opt/collector
export PATH=/usr/local/bin:$PATH
export npm_config_registry=https://registry.npmmirror.com
export npm_config_disturl=https://registry.npmmirror.com/-/binary/node
GYP=$(node -e "console.log(require('path').join(process.execPath,'../lib/node_modules/npm/node_modules/node-gyp/bin/node-gyp.js'))")
ls -la "$GYP"
cd node_modules/better-sqlite3
node "$GYP" rebuild --release --python=/usr/bin/python3

echo "===== [4] swap bundled prebuild with compiled binary ====="
cd /opt/collector/node_modules/better-sqlite3
ls -la build/Release/
cp -f build/Release/better_sqlite3.node prebuilds/linux-x64.node
objdump -T prebuilds/linux-x64.node | grep -oE 'GLIBC_[0-9.]+' | sort -uV | tail -3

echo "===== [5] verify modules ====="
cd /opt/collector
export LD_LIBRARY_PATH=/opt/rh/devtoolset-$DTS/root/usr/lib64:$LD_LIBRARY_PATH
node -e "const D=require('better-sqlite3');const x=new D(':memory:');x.exec('create table t(a)');x.prepare('insert into t values (?)').run(42);console.log('SQLITE_OK', x.prepare('select a from t').get());require('express');require('exceljs');console.log('MODULES_OK')"

echo "===== [6] systemd with DTS runtime lib path ====="
cat > /etc/systemd/system/collector.service <<EOF
[Unit]
Description=Computer Info Collector
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/collector
ExecStart=/usr/local/bin/node /opt/collector/server.js
Restart=always
RestartSec=3
Environment=NODE_ENV=production
Environment=PORT=3000
Environment=LD_LIBRARY_PATH=/opt/rh/devtoolset-$DTS/root/usr/lib64

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl restart collector
sleep 3
systemctl is-active collector
ss -ltnp | grep ':3000' || true
curl -s -o /dev/null -w 'app   127.0.0.1:3000 -> HTTP %{http_code}\n' http://127.0.0.1:3000/api/records
curl -s -o /dev/null -w 'nginx 127.0.0.1:80   -> HTTP %{http_code}\n' http://127.0.0.1/api/records
echo BUILD_DONE
