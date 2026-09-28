#!/bin/bash
export LD_LIBRARY_PATH=/opt/rh/devtoolset-11/root/usr/lib64
cd /opt/collector
/usr/local/bin/node -e '
const D = require("better-sqlite3");
const db = new D("data.db");
const r = db.prepare("DELETE FROM records").run();
console.log("deleted rows:", r.changes);
console.log("remaining:", db.prepare("select count(*) c from records").get().c);
'
echo "--- boot status ---"
systemctl is-enabled collector nginx
systemctl is-active collector nginx
echo "--- timezone ---"
timedatectl 2>/dev/null | grep -E 'Time zone'
echo ALLCLEAN_DONE
