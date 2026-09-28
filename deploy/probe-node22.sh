#!/bin/bash
BASE=https://unofficial-builds.nodejs.org/download/release
for v in $(curl -fsSL $BASE/ | grep -oE 'v22\.[0-9]+\.[0-9]+' | sort -uV | tac | head -25); do
  code=$(curl -fs -o /dev/null -w '%{http_code}' "$BASE/$v/node-$v-linux-x64-glibc-217.tar.xz")
  echo "$v -> $code"
  [ "$code" = "200" ] && break
done
echo PROBE22_DONE
