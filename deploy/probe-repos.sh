#!/bin/bash
echo '-- base --'
curl -fs -o /dev/null -w '%{http_code}\n' http://mirrors.aliyun.com/centos/7.9.2009/os/x86_64/repodata/repomd.xml
echo '-- sclo rh --'
curl -fs -o /dev/null -w '%{http_code}\n' http://mirrors.aliyun.com/centos/sclo/7.9.2009/x86_64/rh/repodata/repomd.xml
curl -fs http://mirrors.aliyun.com/centos/sclo/7.9.2009/x86_64/rh/Packages/d/ 2>/dev/null | grep -oE 'devtoolset-11-gcc-c..-11[^"]*\.rpm' | head -2
echo '-- epel archive --'
curl -fs -o /dev/null -w '%{http_code}\n' https://mirrors.aliyun.com/epel-archive/7/x86_64/repodata/repomd.xml
echo PROBE2_DONE
