#!/bin/bash
for u in \
  http://mirrors.aliyun.com/centos/7/sclo/x86_64/rh/repodata/repomd.xml \
  http://mirrors.aliyun.com/centos/7.9.2009/sclo/x86_64/rh/repodata/repomd.xml \
  https://vault.centos.org/centos/7/sclo/x86_64/rh/repodata/repomd.xml \
  https://vault.centos.org/7.9.2009/sclo/x86_64/rh/repodata/repomd.xml ; do
  code=$(curl -fs -o /dev/null -w '%{http_code}' --max-time 10 "$u")
  echo "$code  $u"
done
echo SCLP_DONE
