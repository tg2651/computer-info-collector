#!/bin/bash
export LD_LIBRARY_PATH=/opt/rh/devtoolset-11/root/usr/lib64
cd /opt/collector
/usr/local/bin/node -e '
const D = require("better-sqlite3");
const db = new D("data.db");
const r = db.prepare("DELETE FROM records WHERE hostname LIKE ?").run("E2E-TEST%");
console.log("deleted test rows:", r.changes);
console.log("remaining:", db.prepare("select count(*) c from records").get().c);
'
echo CLEAN_DONE
