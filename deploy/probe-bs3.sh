#!/bin/bash
# Probe better-sqlite3 prebuilt binaries for glibc/libstdc++ requirements
BASE=https://registry.npmmirror.com/-/binary/better-sqlite3
TMP=/tmp/bs3probe
rm -rf $TMP; mkdir -p $TMP; cd $TMP

for VER in v12.4.1 v11.10.0 v10.1.0 v9.6.0 v9.4.0; do
  echo "================ $VER ================"
  DIR="$BASE/$VER"
  # napi asset first, then node-v115 (Node 20), then v108 (Node 18)
  ASSET=$(curl -fsSL "$DIR/" 2>/dev/null | grep -oE 'better-sqlite3-'$VER'-(napi|node-v115|node-v108)-linux-x64\.tar\.gz' | sort -u | head -3)
  echo "assets: $ASSET"
  for a in $ASSET; do
    echo "-- download $a"
    curl -fsSL "$DIR/$a" -o pkg.tgz || { echo "download failed"; continue; }
    tar -xzf pkg.tgz 2>/dev/null
    BIN=$(find . -name '*.node' | head -1)
    if [ -n "$BIN" ]; then
      GLIBC=$(objdump -T "$BIN" 2>/dev/null | grep -oE 'GLIBC_[0-9.]+' | sort -uV | tail -1)
      GLIBCXX=$(objdump -T "$BIN" 2>/dev/null | grep -oE 'GLIBCXX_[0-9.]+' | sort -uV | tail -1)
      CXXABI=$(objdump -T "$BIN" 2>/dev/null | grep -oE 'CXXABI_[0-9.]+' | sort -uV | tail -1)
      echo "BIN=$BIN  maxGLIBC=$GLIBC maxGLIBCXX=$GLIBCXX maxCXXABI=$CXXABI"
    fi
    rm -f pkg.tgz; find . -name '*.node' -delete
  done
done
echo PROBE_DONE
