#!/bin/bash
TMP=/tmp/bs3v12; rm -rf $TMP; mkdir -p $TMP; cd $TMP
curl -fsSL https://registry.npmmirror.com/better-sqlite3/-/better-sqlite3-12.4.1.tgz -o p.tgz
tar -xzf p.tgz
echo '--- NAPI / engines ---'
grep -E 'NAPI_VERSION' package/build/../binding.gyp 2>/dev/null
grep -E 'NAPI_VERSION' package/binding.gyp
grep -A3 '"engines"' package/package.json
grep -E 'std=c\+\+' package/binding.gyp package/deps/*.gypi 2>/dev/null | head
echo PROBEV12_DONE
