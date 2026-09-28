# 计算机信息收集系统

> 一个面向 IT 资产登记场景的轻量级工具：员工电脑运行采集器（PowerShell 脚本或 .NET 可执行程序）即可一键上报硬件/系统/软件清单，管理员通过后台查看明细、按条件过滤、导出 Excel。

适用于公司内部盘点计算机资产、登记使用人与部门、统计软件安装情况等场景。

## 功能特性

**前端采集（员工侧）**
- 采集：品牌型号、设备类型（笔记本/台式机自动识别）、主机名、序列号、OS 版本/Build、CPU、登录用户、内存规格（插槽/容量/频率/型号）、硬盘（型号/接口/容量/SN）、网卡（MAC/IP）、已安装软件清单
- 上报后自动打开浏览器跳转到登记页，员工只需填两个字段：使用人、部门
- 同一台机器（主机名+序列号）重复上报时复用记录，避免数据重复
- 两种采集器任选其一：
  - `collect.ps1` — PowerShell 5.1+ 脚本，无需编译
  - `collect.exe` — .NET Framework 4.0 编译产物，适用于 PowerShell 被策略禁用的环境（Win7+ 自带 .NET 4）

**后台管理（IT 侧）**
- 登录鉴权（scrypt 密码哈希 + HMAC 签名 Cookie，12 小时有效期）
- 查看所有上报记录列表，按关键字搜索
- 查看单条记录明细（内存/硬盘/网络/软件展开）
- 一键导出全部记录为 Excel（.xlsx）
- 在线修改管理员密码
- 前端采集/登记页面无需登录，对员工透明

## 架构

```
┌─────────────── 员工电脑 ───────────────┐         ┌──────── 服务器 ────────┐
│  collect.ps1  或  collect.exe           │         │  nginx :80              │
│   ├─ WMI 读取硬件                        │  POST   │   └─ 反代到 Node :3000  │
│   ├─ 注册表枚举已安装软件                 │ ──────> │       server.js         │
│   ├─ HTTP POST /api/report              │         │       ├─ Express         │
│   └─ 启动浏览器打开登记页                 │         │       ├─ better-sqlite3  │
└─────────────────────────────────────────┘         │       ├─ ExcelJS        │
                                                    │       └─ data.db (SQLite) │
┌─────────────── 管理员浏览器 ─────────────┐         │                          │
│  /admin      后台记录列表                 │ <─────> │  /api/records (需登录)    │
│  /admin/login 登录页                     │         │  /admin/export (导出 xlsx)│
└─────────────────────────────────────────┘         └──────────────────────────┘
```

技术栈：Node.js + Express 5 + better-sqlite3 + ExcelJS，前端原生 HTML/JS（无框架），数据库 SQLite（零运维）。

## 快速开始

### 1. 服务端部署（Linux）

详见 [`deploy/setup.sh`](deploy/setup.sh)。简要步骤（CentOS 7 / Ubuntu / Debian 通用）：

```bash
# 1. 安装 Node.js 20+ 与构建工具
curl -fsSL https://rpm.nodesource.com/setup_20.x | bash -     # CentOS/RHEL
# 或:  curl -fsSL https://deb.nodesource.com/setup_20.x | bash -   # Debian/Ubuntu
yum install -y nodejs   # 或 apt-get install -y nodejs

# 2. 上传项目文件到 /opt/collector
mkdir -p /opt/collector
cd /opt/collector
# 把 server.js package.json public/ 复制到这里
npm install --omit=dev

# 3. 用 systemd 守护进程（推荐）
cat > /etc/systemd/system/collector.service <<'EOF'
[Unit]
Description=Computer Info Collector
After=network.target

[Service]
WorkingDirectory=/opt/collector
ExecStart=/usr/bin/node server.js
Restart=always
Environment=PORT=3000

[Install]
WantedBy=multi-user.target
EOF
systemctl daemon-reload && systemctl enable --now collector

# 4. 配置 nginx 反向代理
cat > /etc/nginx/conf.d/collector.conf <<'EOF'
server {
    listen 80;
    server_name _;
    client_max_body_size 10m;
    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
EOF
nginx -t && systemctl reload nginx
```

启动后访问 `http://<服务器IP>/admin/login`，默认账号 **admin** / 密码 **admin123**（**首次登录后请立即在右上角"修改密码"修改！**）。

> ⚠️ 强烈建议在 nginx 启用 HTTPS（Let's Encrypt 免费证书），否则员工上报的硬件信息和 Cookie 都将明文传输。

### 2. 员工侧采集

把 `collect.ps1` 或 `collect.exe` 通过内网共享/OA 系统下发给员工，员工双击运行即可。运行后：
1. 控制台显示采集进度
2. 自动 POST 到服务器
3. 自动打开浏览器跳转登记页，员工填入"使用人"和"部门"点提交即可

#### 服务器地址配置

无论是 `collect.ps1` 还是 `collect.exe`，都支持三种方式指定服务器地址（按优先级）：

| 优先级 | 方式 | 示例 |
|---|---|---|
| 1 | 命令行参数 | `collect.exe http://192.168.1.100` |
| 2 | 同目录 `server.txt` 文件 | 第一行写 `http://192.168.1.100` |
| 3 | 首次运行交互输入 | 控制台提示后输入，自动保存到 `server.txt` |

> 推荐方式 2：IT 把 `collect.exe` 和 `server.txt` 一起打包发给员工，员工零配置直接双击。

`server.txt` 格式：
```
http://192.168.1.100
# 井号开头的行是注释，可以写说明
```

### 3. 编译 collect.exe（可选）

仓库**不包含**编译好的 `collect.exe`，请按需自行编译。Win7+ 自带的 .NET Framework 4 已附带 `csc.exe`，无需安装任何 SDK：

```bat
:: 用系统自带的 .NET Framework 4 编译器
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo /platform:anycpu ^
  /r:System.Management.dll ^
  /out:collect.exe ^
  collector-src\collect.cs
```

输出 `collect.exe` 约 19KB，无外部依赖，所有 Windows 7 SP1+ / Win10 / Win11 均可直接运行。

## 后台使用

| 路径 | 说明 |
|---|---|
| `/admin/login` | 管理员登录 |
| `/admin` | 记录列表（搜索、查看详情、导出 Excel） |
| `/admin/export` | 导出全部记录为 `.xlsx` |
| `/api/admin/change-password` | 修改密码（页面右上角入口） |
| `/?id=<guid>` | 员工登记页（员工上报后自动打开） |

## 项目结构

```
.
├── server.js                 # 服务端：API + 鉴权 + SQLite + Excel 导出
├── package.json
├── public/                   # 前端静态资源
│   ├── index.html            # 员工登记页（带 id 参数时）/ 下载引导页（无 id）
│   ├── admin.html            # 后台记录管理页
│   └── login.html            # 管理员登录页
├── collect.ps1               # PowerShell 采集器
├── collector-src/
│   └── collect.cs            # C# 采集器源码（编译为 collect.exe）
├── deploy/                   # 部署辅助脚本
│   ├── setup.sh              # 服务器一键安装
│   ├── 第一步-安装密钥.bat    # 安装 SSH 公钥到服务器（需先填入服务器 IP）
│   └── fix-v12.sh            # CentOS 7 上从源码编译 better-sqlite3 v12
├── 启动服务.bat               # Windows 本地开发启动脚本（用 runtime/ 里的便携 Node）
├── .gitignore
├── LICENSE
└── README.md
```

## 安全说明

- **认证**：管理员密码用 scrypt + 16 字节随机盐哈希存储；登录态用 HMAC-SHA256 签名的 base64url Token，放在 HttpOnly + SameSite=Lax 的 Cookie 中，TTL 12 小时。所有鉴权逻辑只用 Node 内置 `crypto` 模块，零第三方依赖。
- **权限**：前台 `/api/report`（员工上报）与 `/?id=...`（登记页）开放；后台 `/api/records`、`/api/records/:id`、`/admin`、`/admin/export` 全部需要登录。
- **数据**：SQLite 数据库文件 `data.db` 在 `.gitignore` 中，请勿提交到仓库。
- **传输**：默认 HTTP，**强烈建议**在 nginx 启用 HTTPS。

## 采集器原理

- 硬件信息：通过 WMI 查询 `Win32_ComputerSystem` / `Win32_OperatingSystem` / `Win32_BIOS` / `Win32_Processor` / `Win32_PhysicalMemory` / `Win32_DiskDrive` / `Win32_NetworkAdapterConfiguration` / `Win32_SystemEnclosure`
- 已安装软件：枚举注册表 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*`（同时读 64 位和 32 位视图）以及 `HKCU` 下的同名键，过滤掉系统更新（KB 开头）和系统组件
- 设备类型识别：优先看 `Win32_ComputerSystem.PCSystemType`（2 = 移动设备即笔记本），其次看 `Win32_SystemEnclosure.ChassisTypes`（8/9/10/11/12/14/18/21/30/31/32 为笔记本机箱）
- 无需管理员权限即可运行（WMI 和注册表 HKLM 都是普通用户可读）

## 技术栈

| 层 | 选型 |
|---|---|
| 后端 | Node.js 20+, Express 5 |
| 数据库 | better-sqlite3 (SQLite) |
| Excel 导出 | ExcelJS |
| 前端 | 原生 HTML + JavaScript（无框架） |
| 采集器 | PowerShell 5.1 / C# .NET Framework 4.0 |
| Web 服务器 | nginx（反向代理） |
| 鉴权 | scrypt + HMAC-SHA256（Node 内置 crypto） |

## License

本项目基于 [Apache License 2.0](LICENSE) 开源。

```
Copyright 2026 计算机信息收集系统 contributors

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```
