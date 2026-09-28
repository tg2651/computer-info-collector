#!/bin/bash
exec > /root/fix-node.log 2>&1
set -x
BASE=https://unofficial-builds.nodejs.org/download/release

# find the newest v20.x that ships a linux-x64-glibc-217 asset
VERS=""
for v in $(curl -fsSL $BASE/ | grep -oE 'v20\.[0-9]+\.[0-9]+' | sort -uV | tac | head -20); do
  code=$(curl -fs -o /dev/null -w '%{http_code}' "$BASE/$v/node-$v-linux-x64-glibc-217.tar.xz")
  echo "probe $v -> $code"
  if [ "$code" = "200" ]; then VERS=$v; break; fi
done
[ -z "$VERS" ] && { echo NO_GLIBC217_BUILD_FOUND; exit 1; }
echo "USING $VERS"
curl -fsSL "$BASE/$VERS/node-$VERS-linux-x64-glibc-217.tar.xz" -o /tmp/node217.tar.xz
tar -xJf /tmp/node217.tar.xz -C /usr/local --strip-components=1
rm -f /tmp/node217.tar.xz
hash -r
/usr/local/bin/node -v
/usr/local/bin/npm -v

cd /opt/collector
rm -rf node_modules
npm install --omit=dev --no-fund --no-audit

systemctl restart collector
sleep 3
systemctl is-active collector
ss -ltnp | grep ':3000' || true
curl -s -o /dev/null -w 'app   127.0.0.1:3000 -> HTTP %{http_code}\n' http://127.0.0.1:3000/api/records
curl -s -o /dev/null -w 'nginx 127.0.0.1:80   -> HTTP %{http_code}\n' http://127.0.0.1/api/records
echo FIX_DONE
