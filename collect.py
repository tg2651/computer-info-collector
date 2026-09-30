#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Computer Info Collector (macOS / Linux / UOS 版)

采集硬件/系统/软件信息并上报到服务器，然后打开浏览器登记页。

Usage:
    python3 collect.py                                 # 解析服务端地址（见下）
    python3 collect.py http://192.168.1.100:3000       # 指定服务端地址
    python3 collect.py --no-open                       # 不自动打开浏览器
    python3 collect.py --no-pause                       # 结束时不等待

服务端地址解析优先级：
    1) 命令行参数
    2) 同目录 server.txt 文件（首个非空非 # 行）
    3) 首次运行交互输入，自动保存到 server.txt

要求：Python 3.6+（仅标准库，无需 pip install）
"""

import sys
import os
import json
import socket
import platform
import subprocess
import urllib.request
import re
import webbrowser
import argparse


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))


# ---------- 工具：跑系统命令 ----------
def run(cmd, timeout=15):
    """运行命令（str 走 shell，list 直接调用），返回 stdout 字符串，失败返回 ''。"""
    try:
        if isinstance(cmd, str):
            r = subprocess.run(
                cmd, shell=True, capture_output=True,
                text=True, encoding='utf-8', errors='replace',
                timeout=timeout,
            )
        else:
            r = subprocess.run(
                cmd, capture_output=True,
                text=True, encoding='utf-8', errors='replace',
                timeout=timeout,
            )
        return (r.stdout or '').strip()
    except Exception:
        return ''


def run_json(cmd, timeout=15):
    """运行命令并解析 JSON 输出，失败返回 None。"""
    out = run(cmd, timeout=timeout)
    if not out:
        return None
    try:
        return json.loads(out)
    except Exception:
        return None


# ---------- 服务端地址解析 ----------
def resolve_server_url(cli_arg):
    if cli_arg:
        return cli_arg.rstrip('/')
    cfg = os.path.join(SCRIPT_DIR, 'server.txt')
    if os.path.exists(cfg):
        try:
            with open(cfg, 'r', encoding='utf-8') as f:
                for line in f:
                    s = line.strip()
                    if s and not s.startswith('#'):
                        return s.rstrip('/')
        except Exception:
            pass
    # 交互输入
    print('Server address is not configured.')
    print('Enter the server URL (e.g. http://192.168.1.100  or  http://your-server:3000)')
    print('This will be saved to server.txt so you do not need to type it again.')
    try:
        url = input('> ').strip()
    except EOFError:
        url = ''
    if url and not url.startswith('http://') and not url.startswith('https://'):
        url = 'http://' + url
    if url:
        try:
            with open(cfg, 'w', encoding='utf-8') as f:
                f.write(url + '\n# You can edit this line to change the server address.\n')
            print(f'Saved to {cfg}')
        except Exception:
            pass
        print()
    return url.rstrip('/')


# ---------- 平台检测 ----------
def detect_os():
    if sys.platform == 'darwin':
        return 'mac'
    if sys.platform.startswith('linux'):
        return 'linux'
    return 'other'


# ====================== macOS 采集 ======================
def collect_macos():
    # --- 基本信息：用 system_profiler SPHardwareDataType 一次性拿 ---
    sp = run('system_profiler SPHardwareDataType', timeout=20)
    fields = {}
    for line in sp.splitlines():
        if ':' in line:
            k, v = line.split(':', 1)
            fields[k.strip()] = v.strip()

    brand = fields.get('Manufacturer', '') or 'Apple'
    model = fields.get('Model Identifier', '') or fields.get('Model Name', '')
    serial = fields.get('Serial Number (system)', '') or fields.get('Serial Number', '')

    hostname = run('scutil --get ComputerName') or socket.gethostname()

    # --- OS 版本 ---
    mac_ver = platform.mac_ver()
    os_version = f'macOS {mac_ver[0]}' if mac_ver[0] else 'macOS'
    build = run('sw_vers -buildVersion')
    os_build = f'macOS {mac_ver[0]} (Build {build})' if build else os_version

    # --- CPU ---
    cpu = run('sysctl -n machdep.cpu.brand_string')

    # --- 设备类型：Model Identifier 含 "Book" 是笔记本 ---
    device_type = 'Laptop' if 'Book' in model else 'Desktop'

    # --- 登录用户 ---
    logged_user = os.environ.get('USER', '') or run('id -un')

    # --- 内存：总量 + 每条 ---
    mem_total_bytes = run('sysctl -n hw.memsize')
    mem_total_gb = 0
    if mem_total_bytes:
        try:
            mem_total_gb = round(int(mem_total_bytes) / (1024 ** 3), 2)
        except ValueError:
            pass

    memory = []
    # system_profiler SPMemoryDataType 解析每条内存
    sp_mem = run('system_profiler SPMemoryDataType', timeout=20)
    if sp_mem:
        # 按空行分段，每段是一个 DIMM
        blocks = re.split(r'\n\s*\n', sp_mem)
        for block in blocks:
            if 'DIMM' not in block and 'Memory' not in block:
                continue
            slot = ''
            manufacturer = ''
            partno = ''
            capacity_gb = 0
            speed = ''
            for line in block.splitlines():
                if ':' not in line:
                    continue
                k, v = line.split(':', 1)
                k = k.strip()
                v = v.strip()
                if 'DIMM' in k or 'Slot' in k or 'Device Locator' in k:
                    slot = v
                elif k in ('Manufacturer',):
                    manufacturer = v
                elif 'Part Number' in k:
                    partno = v
                elif 'Size' in k or 'Capacity' in k:
                    m = re.search(r'(\d+)\s*GB', v)
                    if m:
                        capacity_gb = int(m.group(1))
                elif 'Speed' in k or 'Frequency' in k:
                    speed = v
            if slot or manufacturer or capacity_gb:
                memory.append({
                    'slot': slot,
                    'manufacturer': manufacturer,
                    'partno': partno,
                    'capacity_gb': capacity_gb,
                    'speed': speed,
                })

    # --- 硬盘：diskutil list + diskutil info ---
    disks = []
    disk_list = run('diskutil list', timeout=10)
    disk_names = re.findall(r'^/dev/(disk\d+)', disk_list, re.MULTILINE)
    seen = set()
    for dev in disk_names:
        if dev in seen:
            continue
        seen.add(dev)
        info_out = run(f'diskutil info -all', timeout=10)
        # diskutil info -all 输出所有磁盘，按 Device Identifier 分段
        # 简化：单独 info 每个磁盘
        info_out = run(f'diskutil info /dev/{dev}', timeout=5)
        model_name = ''
        interface = ''
        size_gb = 0
        serial_no = ''
        for line in info_out.splitlines():
            if ':' not in line:
                continue
            k, v = line.split(':', 1)
            k = k.strip()
            v = v.strip()
            if k in ('Device / Media Name', 'Medium Type', 'Device Location'):
                if not model_name:
                    model_name = v
            elif k == 'Disk Size':
                # "500.1 GB (500107862016 Bytes)"
                m = re.search(r'([\d.]+)\s*GB', v)
                if m:
                    try:
                        size_gb = round(float(m.group(1)))
                    except ValueError:
                        pass
            elif k == 'Protocol':
                interface = v
            elif k == 'Disk / Partition UUID':
                if not serial_no:
                    serial_no = v
        if size_gb or model_name:
            disks.append({
                'model': model_name,
                'interface': interface,
                'size_gb': size_gb,
                'serial': serial_no,
            })

    # --- 网卡：ifconfig + networksetup ---
    network = []
    # 用 networksetup -listallhardwareports 拿接口名
    ns_out = run('networksetup -listallhardwareports', timeout=10)
    # 解析 Hardware Port / Device
    ports = []
    cur_name = ''
    cur_dev = ''
    for line in ns_out.splitlines():
        if line.startswith('Hardware Port:'):
            cur_name = line.split(':', 1)[1].strip()
        elif line.startswith('Device:'):
            cur_dev = line.split(':', 1)[1].strip()
            if cur_dev:
                ports.append((cur_name, cur_dev))
            cur_name = ''
            cur_dev = ''
    for name, dev in ports:
        mac = run(f'ifconfig {dev} 2>/dev/null | grep ether | head -1').replace('ether', '').strip()
        ip_out = run(f'ipconfig getifaddr {dev} 2>/dev/null')
        ip = ip_out if ip_out else ''
        # 仅列出有 MAC 或 IP 的
        if mac or ip:
            network.append({'name': name or dev, 'mac': mac, 'ip': ip})

    # --- 软件：扫描 /Applications 下的 .app ---
    software = []
    apps_dir = '/Applications'
    if os.path.isdir(apps_dir):
        for app in sorted(os.listdir(apps_dir)):
            if not app.endswith('.app'):
                continue
            name = app[:-4]
            plist = os.path.join(apps_dir, app, 'Contents', 'Info.plist')
            if not os.path.exists(plist):
                continue
            version = run(['defaults', 'read', plist, 'CFBundleShortVersionString'], timeout=3)
            if not version:
                version = run(['defaults', 'read', plist, 'CFBundleVersion'], timeout=3)
            publisher = run(['defaults', 'read', plist, 'CFBundleIdentifier'], timeout=3)
            software.append({'name': name, 'version': version, 'publisher': publisher})

    return {
        'hostname': hostname,
        'brand': brand,
        'model': model,
        'device_type': device_type,
        'serial': serial,
        'os_version': os_version,
        'os_build': os_build,
        'cpu': cpu,
        'logged_user': logged_user,
        'memory_total_gb': mem_total_gb,
        'memory': memory,
        'disks': disks,
        'network': network,
        'software': software,
    }


# ====================== Linux / UOS 采集 ======================
def _read_dmi(name):
    """读取 /sys/class/dmi/id/<name>，失败返回 ''。"""
    path = f'/sys/class/dmi/id/{name}'
    try:
        with open(path, 'r', encoding='utf-8') as f:
            return f.read().strip()
    except Exception:
        return ''


def collect_linux():
    # --- 品牌/型号/序列号 ---
    brand = _read_dmi('sys_vendor')
    model = _read_dmi('product_name')
    serial = _read_dmi('product_serial') or _read_dmi('board_serial')

    hostname = socket.gethostname()

    # --- OS 版本 ---
    os_version = ''
    try:
        with open('/etc/os-release', 'r', encoding='utf-8') as f:
            for line in f:
                if line.startswith('PRETTY_NAME='):
                    os_version = line.split('=', 1)[1].strip().strip('"')
                    break
    except Exception:
        pass
    if not os_version:
        os_version = platform.platform()
    kernel = run('uname -r')
    os_build = f'{os_version} (Kernel {kernel})' if kernel else os_version

    # --- CPU ---
    cpu = ''
    try:
        with open('/proc/cpuinfo', 'r', encoding='utf-8') as f:
            for line in f:
                if line.startswith('model name'):
                    cpu = line.split(':', 1)[1].strip()
                    break
    except Exception:
        pass
    if not cpu:
        # 退回 lscpu
        lscpu = run('lscpu', timeout=5)
        for line in lscpu.splitlines():
            if line.startswith('Model name'):
                cpu = line.split(':', 1)[1].strip()
                break

    # --- 设备类型 ---
    chassis = _read_dmi('chassis_type')
    laptop_types = {'8', '9', '10', '11', '12', '14', '18', '21', '30', '31', '32'}
    desktop_types = {'3', '4', '5', '6', '7', '13', '15', '16', '17', '23', '24'}
    device_type = 'Desktop'
    if chassis in laptop_types:
        device_type = 'Laptop'
    elif chassis in desktop_types:
        device_type = 'Desktop'

    # --- 登录用户 ---
    logged_user = os.environ.get('USER', '') or run('id -un')

    # --- 内存：总量 + 每条 ---
    mem_total_kb = 0
    try:
        with open('/proc/meminfo', 'r', encoding='utf-8') as f:
            for line in f:
                if line.startswith('MemTotal:'):
                    parts = line.split()
                    if len(parts) >= 2:
                        mem_total_kb = int(parts[1])
                    break
    except Exception:
        pass
    mem_total_gb = round(mem_total_kb / (1024 * 1024), 2) if mem_total_kb else 0

    # 每条内存需 dmidecode（需 root，普通用户可能拿不到）
    memory = []
    dmi = run('dmidecode -t memory 2>/dev/null', timeout=10)
    if dmi:
        # 按 "Memory Device" 分段
        blocks = re.split(r'\n\s*\n', dmi)
        for block in blocks:
            if 'Memory Device' not in block:
                continue
            slot = ''
            manufacturer = ''
            partno = ''
            capacity_gb = 0
            speed = ''
            for line in block.splitlines():
                if ':' not in line:
                    continue
                k, v = line.split(':', 1)
                k = k.strip()
                v = v.strip()
                if k == 'Locator':
                    slot = v
                elif k == 'Manufacturer':
                    manufacturer = v
                elif k == 'Part Number':
                    partno = v
                elif k == 'Size':
                    m = re.search(r'(\d+)\s*GB', v)
                    if m:
                        capacity_gb = int(m.group(1))
                elif k == 'Configured Memory Speed' or k == 'Speed':
                    if 'No Module' not in v:
                        speed = v
            if slot or manufacturer or capacity_gb:
                memory.append({
                    'slot': slot,
                    'manufacturer': manufacturer,
                    'partno': partno,
                    'capacity_gb': capacity_gb,
                    'speed': speed,
                })

    # --- 硬盘：lsblk -J 拿 JSON ---
    disks = []
    blk_json = run_json(['lsblk', '-d', '-b', '-o', 'NAME,MODEL,SERIAL,SIZE,TYPE,ROTA', '-J'], timeout=10)
    if blk_json and 'blockdevices' in blk_json:
        for dev in blk_json['blockdevices']:
            if dev.get('type') != 'disk':
                continue
            size_b = dev.get('size', 0) or 0
            try:
                size_gb = round(int(size_b) / (1024 ** 3))
            except (ValueError, TypeError):
                size_gb = 0
            rota = dev.get('rota')
            interface = 'HDD' if rota == 1 else 'SSD' if rota == 0 else ''
            disks.append({
                'model': dev.get('model', '') or '',
                'interface': interface,
                'size_gb': size_gb,
                'serial': dev.get('serial', '') or '',
            })
    else:
        # 退回 /sys/block
        for name in os.listdir('/sys/block') if os.path.isdir('/sys/block') else []:
            if name.startswith(('loop', 'ram', 'sr')):
                continue
            model_path = f'/sys/block/{name}/device/model'
            serial_path = f'/sys/block/{name}/device/serial'
            size_path = f'/sys/block/{name}/size'
            model = ''
            serial = ''
            size_gb = 0
            try:
                with open(model_path) as f:
                    model = f.read().strip()
            except Exception:
                pass
            try:
                with open(serial_path) as f:
                    serial = f.read().strip()
            except Exception:
                pass
            try:
                with open(size_path) as f:
                    sectors = int(f.read().strip())
                    size_gb = round(sectors * 512 / (1024 ** 3))
            except Exception:
                pass
            if size_gb or model:
                disks.append({
                    'model': model,
                    'interface': '',
                    'size_gb': size_gb,
                    'serial': serial,
                })

    # --- 网卡：/sys/class/net + ip addr ---
    network = []
    net_dir = '/sys/class/net'
    if os.path.isdir(net_dir):
        for iface in sorted(os.listdir(net_dir)):
            if iface == 'lo':
                continue
            mac = ''
            try:
                with open(f'{net_dir}/{iface}/address') as f:
                    mac = f.read().strip()
            except Exception:
                pass
            ip = ''
            ip_out = run(['ip', '-4', 'addr', 'show', iface], timeout=3)
            m = re.search(r'inet (\d+\.\d+\.\d+\.\d+)', ip_out)
            if m:
                ip = m.group(1)
            if mac or ip:
                network.append({'name': iface, 'mac': mac, 'ip': ip})

    # --- 软件：dpkg-query（UOS 是 Debian 系） ---
    software = []
    dpkg_out = run(['dpkg-query', '-W', '-f=${Package}\t${Version}\t${Maintainer}\n'], timeout=30)
    if dpkg_out:
        # 过滤系统库、语言包等
        skip_prefixes = (
            'lib', 'gir1.', 'python3-', 'python-', 'fonts-', 'language-pack-',
            'iso-codes', 'shared-mime-info', 'xdg-', 'hicolor-icon-theme',
            'perl-', 'perl_modules-', 'g++-', 'gcc-', 'binutils-',
        )
        for line in dpkg_out.splitlines():
            parts = line.split('\t')
            if len(parts) < 2:
                continue
            name = parts[0]
            version = parts[1]
            publisher = parts[2] if len(parts) > 2 else ''
            if not name or name.startswith(skip_prefixes):
                continue
            software.append({'name': name, 'version': version, 'publisher': publisher})

    return {
        'hostname': hostname,
        'brand': brand,
        'model': model,
        'device_type': device_type,
        'serial': serial,
        'os_version': os_version,
        'os_build': os_build,
        'cpu': cpu,
        'logged_user': logged_user,
        'memory_total_gb': mem_total_gb,
        'memory': memory,
        'disks': disks,
        'network': network,
        'software': software,
    }


# ====================== 主流程 ======================
def main():
    parser = argparse.ArgumentParser(description='计算机信息采集器（macOS / Linux / UOS）')
    parser.add_argument('server', nargs='?', default='', help='服务端地址，如 http://192.168.1.100:3000')
    parser.add_argument('--no-open', action='store_true', help='不自动打开浏览器')
    parser.add_argument('--no-pause', action='store_true', help='结束时不要等待')
    args = parser.parse_args()

    server_url = resolve_server_url(args.server)
    if not server_url:
        print('FAILED: No server address provided.')
        print('Usage:')
        print('  python3 collect.py http://your-server')
        print('  Or place the URL in a server.txt file next to collect.py.')
        if not args.no_pause:
            try:
                input('Press Enter to exit...')
            except EOFError:
                pass
        sys.exit(1)

    print('=' * 46)
    print(' Computer Info Collector')
    print('=' * 46)
    print(f' Server: {server_url}')
    print('=' * 46)

    try:
        print('[1/4] Collecting system information...')
        os_type = detect_os()
        if os_type == 'mac':
            info = collect_macos()
        elif os_type == 'linux':
            info = collect_linux()
        else:
            raise RuntimeError(f'Unsupported platform: {sys.platform}. '
                               f'This script supports macOS and Linux/UOS only. '
                               f'On Windows, use collect.exe or collect.ps1 instead.')

        print('[2/4] Collecting installed software list...')
        # 软件在 collect_macos/linux 内已采集，这里只打印

        print(f'[3/4] Found: {len(info["memory"])} memory stick(s), '
              f'{len(info["disks"])} disk(s), '
              f'{len(info["network"])} network adapter(s), '
              f'{len(info["software"])} software item(s).')

        print(f'[4/4] Reporting to {server_url} ...')
        body = json.dumps(info, ensure_ascii=False).encode('utf-8')
        req = urllib.request.Request(
            f'{server_url}/api/report',
            data=body,
            headers={'Content-Type': 'application/json; charset=utf-8'},
            method='POST',
        )
        with urllib.request.urlopen(req, timeout=30) as resp:
            r = json.loads(resp.read().decode('utf-8'))

        if r.get('ok') and r.get('id'):
            url = f'{server_url}/?id={r["id"]}'
            print()
            print(f'SUCCESS! Record id: {r["id"]}')
            print(f'Opening registration page: {url}')
            if not args.no_open:
                webbrowser.open(url)
        else:
            raise RuntimeError('Server returned an unexpected response.')
    except urllib.error.HTTPError as e:
        body_text = ''
        try:
            body_text = e.read().decode('utf-8', errors='replace')
        except Exception:
            pass
        print()
        print(f'FAILED: HTTP {e.code} {e.reason}')
        if body_text:
            print(f'Server response: {body_text[:500]}')
        print('Please check: 1) server address is correct  '
              '2) server is running  3) network/firewall allows this connection.')
        if not args.no_pause:
            try:
                input('Press Enter to exit...')
            except EOFError:
                pass
        sys.exit(1)
    except Exception as e:
        print()
        print(f'FAILED: {e}')
        print('Please check: 1) server address is correct  '
              '2) server is running  3) network/firewall allows this connection.')
        if not args.no_pause:
            try:
                input('Press Enter to exit...')
            except EOFError:
                pass
        sys.exit(1)

    if not args.no_pause:
        try:
            input('Press Enter to exit...')
        except EOFError:
            pass


if __name__ == '__main__':
    main()
