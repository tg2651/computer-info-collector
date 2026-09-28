#!/bin/bash
# Hardened setup for CentOS 7 (EOL): vault yum fix, official Node 20 tarball,
# nginx.org repo, systemd service, nginx reverse proxy 80 -> 3000.
exec > /root/setup.log 2>&1
set -x

APP_DIR=/opt/collector
SERVICE=collector

echo "===== [0] CentOS 7 EOL repo fix (vault) ====="
if grep -q 'CentOS Linux release 7' /etc/redhat-release 2>/dev/null; then
  for f in /etc/yum.repos.d/CentOS-*.repo; do
    [ -f "$f" ] || continue
    sed -i -e 's/^mirrorlist=/#mirrorlist=/g' -e 's|^#baseurl=http://mirror.centos.org|baseurl=http://vault.centos.org|g' "$f"
  done
fi
yum clean all
yum makecache -y || true

echo "===== [1] Node.js 20 (official prebuilt tarball) ====="
if ! command -v node >/dev/null 2>&1; then
  NODE_FILE=$(curl -fsSL https://nodejs.org/dist/latest-v20.x/ | grep -oE 'node-v[0-9.]+-linux-x64\.tar\.xz' | head -1)
  echo "downloading $NODE_FILE"
  curl -fsSL "https://nodejs.org/dist/latest-v20.x/$NODE_FILE" -o /tmp/node.tar.xz
  tar -xJf /tmp/node.tar.xz -C /usr/local --strip-components=1
  rm -f /tmp/node.tar.xz
fi
node -v
npm -v

echo "===== [2] nginx (official nginx.org repo, EPEL fallback) ====="
if ! command -v nginx >/dev/null 2>&1; then
  cat > /etc/yum.repos.d/nginx.repo <<'EOF'
[nginx]
name=nginx official repo
baseurl=http://nginx.org/packages/centos/7/$basearch/
gpgcheck=0
enabled=1
EOF
  if ! yum install -y nginx; then
    yum install -y epel-release || true
    sed -i -e 's/^mirrorlist=/#mirrorlist=/g' -e 's|^#baseurl=https://download.fedoraproject.org/pub/epel|baseurl=https://archives.fedoraproject.org/pub/epel-archive|g' /etc/yum.repos.d/epel.repo 2>/dev/null || true
    yum install -y nginx
  fi
fi
nginx -v

echo "===== [3] deploy app ====="
mkdir -p $APP_DIR
tar -xzf /root/collector-app.tar.gz -C $APP_DIR
cd $APP_DIR
npm install --omit=dev --no-fund --no-audit

echo "===== [4] systemd service ====="
cat > /etc/systemd/system/${SERVICE}.service <<EOF
[Unit]
Description=Computer Info Collector
After=network.target

[Service]
Type=simple
WorkingDirectory=$APP_DIR
ExecStart=/usr/local/bin/node $APP_DIR/server.js
Restart=always
RestartSec=3
Environment=NODE_ENV=production
Environment=PORT=3000

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload
systemctl enable $SERVICE
systemctl restart $SERVICE
sleep 3
systemctl --no-pager --full status $SERVICE | head -10
ss -ltnp | grep ':3000' || true

echo "===== [5] nginx config ====="
cat > /etc/nginx/nginx.conf <<'EOF'
user nginx;
worker_processes auto;
error_log /var/log/nginx/error.log notice;
pid /run/nginx.pid;

events {
    worker_connections 1024;
}

http {
    include       /etc/nginx/mime.types;
    default_type  application/octet-stream;
    sendfile        on;
    keepalive_timeout 65;
    gzip on;
    gzip_types text/plain text/css application/json application/javascript;

    include /etc/nginx/conf.d/*.conf;
}
EOF

cat > /etc/nginx/conf.d/${SERVICE}.conf <<'EOF'
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name _;
    client_max_body_size 6m;

    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 60s;
    }
}
EOF
# allow nginx to proxy to local port when SELinux is enforcing
if command -v setsebool >/dev/null 2>&1; then
  setsebool -P httpd_can_network_connect 1 2>/dev/null || true
fi
nginx -t
systemctl enable nginx
systemctl restart nginx

echo "===== [6] firewall ====="
if command -v firewall-cmd >/dev/null 2>&1 && firewall-cmd --state 2>/dev/null | grep -qi running; then
  firewall-cmd --permanent --add-port=80/tcp
  firewall-cmd --permanent --add-service=http
  firewall-cmd --reload
fi

echo "===== [7] local checks ====="
sleep 1
curl -s -o /dev/null -w "app   127.0.0.1:3000 -> HTTP %{http_code}\n" http://127.0.0.1:3000/api/records
curl -s -o /dev/null -w "nginx 127.0.0.1:80   -> HTTP %{http_code}\n" http://127.0.0.1/api/records
echo "SETUP_DONE"
