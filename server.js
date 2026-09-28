const express = require('express');
const path = require('path');
const crypto = require('crypto');
const Database = require('better-sqlite3');
const ExcelJS = require('exceljs');

const app = express();
const PORT = process.env.PORT || 3000;
// 数据库目录：本地默认写在项目根目录，Docker 通过 DATA_DIR=/app/data 挂卷持久化
const DATA_DIR = process.env.DATA_DIR || __dirname;

// ---------- 数据库 ----------
const db = new Database(path.join(DATA_DIR, 'data.db'));
db.pragma('journal_mode = WAL');
db.exec(`
CREATE TABLE IF NOT EXISTS records (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  guid TEXT UNIQUE NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending',
  reported_at TEXT,
  submitted_at TEXT,
  brand TEXT DEFAULT '',
  model TEXT DEFAULT '',
  device_type TEXT DEFAULT '',
  hostname TEXT DEFAULT '',
  serial TEXT DEFAULT '',
  os_version TEXT DEFAULT '',
  os_build TEXT DEFAULT '',
  cpu TEXT DEFAULT '',
  logged_user TEXT DEFAULT '',
  memory_total_gb TEXT DEFAULT '',
  memory_json TEXT DEFAULT '[]',
  disk_json TEXT DEFAULT '[]',
  network_json TEXT DEFAULT '[]',
  software_json TEXT DEFAULT '[]',
  user_name TEXT DEFAULT '',
  department TEXT DEFAULT ''
);
CREATE TABLE IF NOT EXISTS settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
`);

// ---------- 工具 ----------
function s(v, max = 300) {
  if (v === undefined || v === null) return '';
  return String(v).trim().slice(0, max);
}

function fmtNow(d = new Date()) {
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}:${p(d.getSeconds())}`;
}

function parseJson(str, fallback) {
  try {
    const v = JSON.parse(str);
    return Array.isArray(v) ? v : fallback;
  } catch {
    return fallback;
  }
}

// 同一台机器（主机名+序列号）重复上报时复用同一条记录，避免重复
function makeGuid(hostname, serial) {
  const raw = `${s(hostname).toUpperCase()}_${s(serial)}`;
  const clean = raw.replace(/[^A-Za-z0-9_-]+/g, '-').replace(/^-+|-+$/g, '');
  return clean || crypto.randomUUID();
}

function rowOut(row, withJson = false) {
  if (!row) return null;
  const base = {
    id: row.id,
    guid: row.guid,
    status: row.status,
    reported_at: row.reported_at,
    submitted_at: row.submitted_at,
    brand: row.brand,
    model: row.model,
    device_type: row.device_type,
    hostname: row.hostname,
    serial: row.serial,
    os_version: row.os_version,
    os_build: row.os_build,
    cpu: row.cpu,
    logged_user: row.logged_user,
    memory_total_gb: row.memory_total_gb,
    user_name: row.user_name,
    department: row.department,
  };
  const network = parseJson(row.network_json, []);
  base.network = network;
  base.ip = [...new Set(network.map((n) => n.ip).filter(Boolean))].join(', ');
  if (withJson) {
    base.memory = parseJson(row.memory_json, []);
    base.disks = parseJson(row.disk_json, []);
    base.software = parseJson(row.software_json, []);
  } else {
    base.disk_count = parseJson(row.disk_json, []).length;
    base.software_count = parseJson(row.software_json, []).length;
  }
  return base;
}

// ---------- 后台认证（scrypt 密码哈希 + HMAC Cookie Token，无第三方依赖） ----------
const ADMIN_USERNAME = 'admin';
const DEFAULT_PASSWORD = 'admin123';
const TOKEN_TTL_MS = 12 * 60 * 60 * 1000;

function getSetting(key) {
  const row = db.prepare('SELECT value FROM settings WHERE key = ?').get(key);
  return row ? row.value : null;
}
function setSetting(key, value) {
  db.prepare('INSERT INTO settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value').run(key, value);
}

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString('hex');
  const hash = crypto.scryptSync(String(password), salt, 64).toString('hex');
  return `${salt}:${hash}`;
}
function verifyPassword(password, stored) {
  if (!stored || !stored.includes(':')) return false;
  const [salt, hash] = stored.split(':');
  let hashBuf;
  try { hashBuf = Buffer.from(hash, 'hex'); } catch { return false; }
  const test = crypto.scryptSync(String(password), salt, 64);
  if (test.length !== hashBuf.length) return false;
  return crypto.timingSafeEqual(hashBuf, test);
}

// 首次启动：初始密码与签名密钥
if (!getSetting('admin_password')) setSetting('admin_password', hashPassword(DEFAULT_PASSWORD));
let TOKEN_SECRET = getSetting('token_secret');
if (!TOKEN_SECRET) {
  TOKEN_SECRET = crypto.randomBytes(32).toString('hex');
  setSetting('token_secret', TOKEN_SECRET);
}

function signToken() {
  const payload = Buffer.from(JSON.stringify({ exp: Date.now() + TOKEN_TTL_MS })).toString('base64url');
  const sig = crypto.createHmac('sha256', TOKEN_SECRET).update(payload).digest('base64url');
  return `${payload}.${sig}`;
}
function verifyToken(token) {
  if (!token || typeof token !== 'string' || token.indexOf('.') < 0) return false;
  const dot = token.lastIndexOf('.');
  const payload = token.slice(0, dot);
  const sig = token.slice(dot + 1);
  let expect;
  try { expect = crypto.createHmac('sha256', TOKEN_SECRET).update(payload).digest('base64url'); } catch { return false; }
  if (Buffer.byteLength(sig) !== Buffer.byteLength(expect)) return false;
  if (!crypto.timingSafeEqual(Buffer.from(sig), Buffer.from(expect))) return false;
  try {
    const data = JSON.parse(Buffer.from(payload, 'base64url').toString());
    return typeof data.exp === 'number' && data.exp > Date.now();
  } catch { return false; }
}
function parseCookies(header) {
  const out = {};
  if (!header) return out;
  header.split(';').forEach((p) => {
    const i = p.indexOf('=');
    if (i > 0) out[p.slice(0, i).trim()] = decodeURIComponent(p.slice(i + 1).trim());
  });
  return out;
}
function isAuthed(req) {
  return verifyToken(parseCookies(req.headers.cookie).admin_token);
}
function authCookie(token) {
  return `admin_token=${token}; HttpOnly; Path=/; Max-Age=${Math.round(TOKEN_TTL_MS / 1000)}; SameSite=Lax`;
}
function clearAuthCookie() {
  return 'admin_token=; HttpOnly; Path=/; Max-Age=0; SameSite=Lax';
}
// 统一鉴权中间件：API 返回 401 JSON，页面跳转登录页
function requireAdmin(req, res, next) {
  if (isAuthed(req)) return next();
  if (req.path.startsWith('/api/')) return res.status(401).json({ ok: false, error: '未登录或登录已过期' });
  return res.redirect('/admin/login');
}

app.use(express.json({ limit: '4mb' }));
// 防止直接访问 /admin.html 绕过 /admin 守卫
app.use((req, res, next) => {
  if (req.path === '/admin.html' && !isAuthed(req)) return res.redirect('/admin/login');
  next();
});
app.use(express.static(path.join(__dirname, 'public'), {
  setHeaders(res, filePath) {
    if (filePath.endsWith('.html')) res.setHeader('Cache-Control', 'no-cache');
    if (filePath.endsWith('.exe')) res.setHeader('Content-Disposition', 'attachment; filename="' + path.basename(filePath) + '"');
  },
}));

// ---------- 后台登录 / 登出 / 会话 / 修改密码 ----------
app.post('/api/admin/login', (req, res) => {
  const username = s(req.body.username, 50);
  const password = req.body.password === undefined || req.body.password === null ? '' : String(req.body.password).slice(0, 100);
  if (username !== ADMIN_USERNAME || !verifyPassword(password, getSetting('admin_password'))) {
    return res.status(401).json({ ok: false, error: '账号或密码错误' });
  }
  res.setHeader('Set-Cookie', authCookie(signToken()));
  res.json({ ok: true, username: ADMIN_USERNAME });
});

app.post('/api/admin/logout', (req, res) => {
  res.setHeader('Set-Cookie', clearAuthCookie());
  res.json({ ok: true });
});

app.get('/api/admin/session', requireAdmin, (req, res) => {
  res.json({ ok: true, username: ADMIN_USERNAME });
});

app.post('/api/admin/change-password', requireAdmin, (req, res) => {
  const oldPwd = req.body.old_password === undefined || req.body.old_password === null ? '' : String(req.body.old_password).slice(0, 100);
  const newPwd = req.body.new_password === undefined || req.body.new_password === null ? '' : String(req.body.new_password).slice(0, 100);
  if (!verifyPassword(oldPwd, getSetting('admin_password'))) {
    return res.status(400).json({ ok: false, error: '原密码错误' });
  }
  if (newPwd.length < 6) {
    return res.status(400).json({ ok: false, error: '新密码至少 6 位' });
  }
  setSetting('admin_password', hashPassword(newPwd));
  res.json({ ok: true });
});

// ---------- 信息上报（collect.ps1 / collect.exe 调用） ----------
function normDeviceType(v) {
  const t = s(v).toLowerCase();
  if (['laptop', 'mobile', 'notebook', 'portable', '笔记本', '笔记本电脑'].includes(t)) return '笔记本';
  if (['desktop', 'pc', '台式机', '台式电脑'].includes(t)) return '台式机';
  return s(v);
}

app.post('/api/report', (req, res) => {
  const b = req.body || {};
  if (!s(b.hostname)) return res.status(400).json({ ok: false, error: 'hostname required' });

  const guid = makeGuid(b.hostname, b.serial);
  const now = fmtNow();
  const data = {
    brand: s(b.brand),
    model: s(b.model),
    device_type: normDeviceType(b.device_type),
    hostname: s(b.hostname),
    serial: s(b.serial),
    os_version: s(b.os_version),
    os_build: s(b.os_build),
    cpu: s(b.cpu),
    logged_user: s(b.logged_user),
    memory_total_gb: s(b.memory_total_gb),
    memory_json: JSON.stringify(Array.isArray(b.memory) ? b.memory : []),
    disk_json: JSON.stringify(Array.isArray(b.disks) ? b.disks : []),
    network_json: JSON.stringify(Array.isArray(b.network) ? b.network : []),
    software_json: JSON.stringify(Array.isArray(b.software) ? b.software : []),
  };

  const cols = Object.keys(data).join(', ');
  const holders = Object.keys(data).map((k) => `@${k}`).join(', ');
  const existing = db.prepare('SELECT id FROM records WHERE guid = ?').get(guid);
  if (existing) {
    // 硬件信息更新，已填写的使用人/部门保留
    db.prepare(`UPDATE records SET reported_at = @reported_at, ${Object.keys(data).map((k) => `${k} = @${k}`).join(', ')} WHERE id = @id`)
      .run({ ...data, reported_at: now, id: existing.id });
  } else {
    db.prepare(`INSERT INTO records (guid, status, reported_at, ${cols}) VALUES (@guid, 'pending', @reported_at, ${holders})`)
      .run({ ...data, guid, reported_at: now });
  }
  res.json({ ok: true, id: guid });
});

// ---------- 前台：按 guid 查询信息 ----------
app.get('/api/info', (req, res) => {
  const row = db.prepare('SELECT * FROM records WHERE guid = ?').get(s(req.query.id, 200));
  if (!row) return res.status(404).json({ ok: false, error: '未找到该记录，请先在目标电脑上运行 collect.ps1' });
  res.json({ ok: true, data: rowOut(row, true) });
});

// ---------- 前台：提交使用人/部门 ----------
app.post('/api/submit', (req, res) => {
  const { id, user_name, department } = req.body || {};
  const user = s(user_name, 100);
  const dept = s(department, 100);
  if (!id) return res.status(400).json({ ok: false, error: '缺少 id' });
  if (!user) return res.status(400).json({ ok: false, error: '请填写使用人' });
  if (!dept) return res.status(400).json({ ok: false, error: '请填写部门' });

  const row = db.prepare('SELECT id, status FROM records WHERE guid = ?').get(s(id, 200));
  if (!row) return res.status(404).json({ ok: false, error: '记录不存在' });
  if (row.status === 'submitted') return res.status(409).json({ ok: false, error: '该记录已提交，请勿重复提交' });

  db.prepare("UPDATE records SET user_name = ?, department = ?, status = 'submitted', submitted_at = ? WHERE id = ?")
    .run(user, dept, fmtNow(), row.id);
  res.json({ ok: true });
});

// ---------- 后台：记录列表（需登录） ----------
app.get('/api/records', requireAdmin, (req, res) => {
  const q = s(req.query.q, 100);
  let rows;
  if (q) {
    const like = `%${q}%`;
    rows = db.prepare(`
      SELECT * FROM records
      WHERE hostname LIKE ? OR brand LIKE ? OR model LIKE ? OR user_name LIKE ?
         OR department LIKE ? OR serial LIKE ? OR logged_user LIKE ? OR device_type LIKE ?
      ORDER BY reported_at DESC
    `).all(like, like, like, like, like, like, like, like);
  } else {
    rows = db.prepare('SELECT * FROM records ORDER BY reported_at DESC').all();
  }
  res.json({ ok: true, data: rows.map((r) => rowOut(r)) });
});

// ---------- 后台：单条详情（需登录） ----------
app.get('/api/records/:id', requireAdmin, (req, res) => {
  const row = db.prepare('SELECT * FROM records WHERE id = ?').get(Number(req.params.id) || 0);
  if (!row) return res.status(404).json({ ok: false, error: '记录不存在' });
  res.json({ ok: true, data: rowOut(row, true) });
});

// ---------- 后台登录页 ----------
app.get('/admin/login', (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'login.html'));
});

// ---------- 后台页面（需登录） ----------
app.get('/admin', requireAdmin, (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'admin.html'));
});

// ---------- 后台：导出 Excel（需登录，支持时间段筛选） ----------
app.get('/admin/export', requireAdmin, async (req, res) => {
  const dateRe = /^\d{4}-\d{2}-\d{2}$/;
  const from = dateRe.test(s(req.query.from, 10)) ? s(req.query.from, 10) : '';
  const to = dateRe.test(s(req.query.to, 10)) ? s(req.query.to, 10) : '';
  let rows;
  if (from && to) {
    rows = db.prepare('SELECT * FROM records WHERE reported_at >= ? AND reported_at <= ? ORDER BY reported_at DESC')
      .all(from + ' 00:00:00', to + ' 23:59:59');
  } else if (from) {
    rows = db.prepare('SELECT * FROM records WHERE reported_at >= ? ORDER BY reported_at DESC').all(from + ' 00:00:00');
  } else if (to) {
    rows = db.prepare('SELECT * FROM records WHERE reported_at <= ? ORDER BY reported_at DESC').all(to + ' 23:59:59');
  } else {
    rows = db.prepare('SELECT * FROM records ORDER BY reported_at DESC').all();
  }

  const wb = new ExcelJS.Workbook();
  wb.creator = '计算机信息收集系统';

  // Sheet1 资产信息
  const ws = wb.addWorksheet('资产信息');
  ws.columns = [
    { header: '上报时间', key: 'reported_at', width: 20 },
    { header: '提交时间', key: 'submitted_at', width: 20 },
    { header: '状态', key: 'status', width: 10 },
    { header: '主机名', key: 'hostname', width: 16 },
    { header: '设备类型', key: 'device_type', width: 10 },
    { header: '品牌', key: 'brand', width: 14 },
    { header: '型号', key: 'model', width: 22 },
    { header: '设备序列号', key: 'serial', width: 20 },
    { header: 'CPU', key: 'cpu', width: 36 },
    { header: '操作系统', key: 'os_version', width: 24 },
    { header: '系统版本号', key: 'os_build', width: 16 },
    { header: '当前登录用户', key: 'logged_user', width: 18 },
    { header: '使用人', key: 'user_name', width: 12 },
    { header: '部门', key: 'department', width: 16 },
    { header: '内存总量(GB)', key: 'memory_total_gb', width: 13 },
    { header: '内存明细', key: 'memory_detail', width: 40 },
    { header: '硬盘明细', key: 'disk_detail', width: 46 },
    { header: '网卡/IP/MAC', key: 'net_detail', width: 40 },
    { header: '软件数量', key: 'software_count', width: 10 },
  ];
  ws.getRow(1).font = { bold: true, color: { argb: 'FFFFFFFF' } };
  ws.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4472C4' } };
  ws.getRow(1).alignment = { vertical: 'middle', horizontal: 'center' };
  ws.views = [{ state: 'frozen', ySplit: 1 }];

  // Sheet2 软件清单
  const ws2 = wb.addWorksheet('软件清单');
  ws2.columns = [
    { header: '主机名', key: 'hostname', width: 16 },
    { header: '使用人', key: 'user_name', width: 12 },
    { header: '部门', key: 'department', width: 16 },
    { header: '软件名称', key: 'name', width: 44 },
    { header: '版本', key: 'version', width: 18 },
    { header: '发布者', key: 'publisher', width: 30 },
  ];
  ws2.getRow(1).font = { bold: true, color: { argb: 'FFFFFFFF' } };
  ws2.getRow(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4472C4' } };
  ws2.views = [{ state: 'frozen', ySplit: 1 }];

  for (const r of rows) {
    const memory = parseJson(r.memory_json, []);
    const disks = parseJson(r.disk_json, []);
    const network = parseJson(r.network_json, []);
    const software = parseJson(r.software_json, []);

    ws.addRow({
      reported_at: r.reported_at,
      submitted_at: r.submitted_at,
      status: r.status === 'submitted' ? '已提交' : '待提交',
      hostname: r.hostname,
      device_type: r.device_type,
      brand: r.brand,
      model: r.model,
      serial: r.serial,
      cpu: r.cpu,
      os_version: r.os_version,
      os_build: r.os_build,
      logged_user: r.logged_user,
      user_name: r.user_name,
      department: r.department,
      memory_total_gb: r.memory_total_gb,
      memory_detail: memory.map((m) => `${m.slot || ''} ${m.capacity_gb || '?'}GB ${m.speed || '?'}MHz ${m.manufacturer || ''} ${m.partno || ''}`.trim()).join('\n'),
      disk_detail: disks.map((d) => `${d.model || ''} ${d.size_gb || '?'}GB SN:${d.serial || ''}`).join('\n'),
      net_detail: network.map((n) => `${n.name || ''} IP:${n.ip || ''} MAC:${n.mac || ''}`).join('\n'),
      software_count: software.length,
    }).eachCell((cell) => { cell.alignment = { vertical: 'top', wrapText: true }; });

    for (const sw of software) {
      ws2.addRow({
        hostname: r.hostname,
        user_name: r.user_name,
        department: r.department,
        name: sw.name || '',
        version: sw.version || '',
        publisher: sw.publisher || '',
      });
    }
  }

  const d = new Date();
  const p = (n) => String(n).padStart(2, '0');
  const datePart = (from && to) ? `${from.replace(/-/g, '')}-${to.replace(/-/g, '')}` : `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}`;
  const filename = `计算机信息_${datePart}.xlsx`;

  const buffer = await wb.xlsx.writeBuffer();
  res.setHeader('Content-Type', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
  res.setHeader('Content-Disposition', `attachment; filename*=UTF-8''${encodeURIComponent(filename)}`);
  res.send(Buffer.from(buffer));
});

app.listen(PORT, '0.0.0.0', () => {
  console.log(`计算机信息收集系统已启动: http://localhost:${PORT}`);
  console.log(`后台管理:            http://localhost:${PORT}/admin`);
});
