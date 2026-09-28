// ==============================================================
//  Computer Info Collector - WinForms GUI edition
//  Provides a graphical interface for:
//    1) Configuring the server URL (saved to server.txt)
//    2) Testing connectivity to the server
//    3) Collecting hardware/OS/software info and reporting to server
//    4) Opening the registration page in the default browser
//
//  Requires: .NET Framework 4.0+ (preinstalled on Win7+)
//  Build:
//    csc /nologo /platform:anycpu /r:System.Management.dll ^
//        /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
//        /out:collect-ui.exe collect-ui.cs
//
//  Console version (collect.exe) is also available for scripted use.
// ==============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Management;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal class CollectorForm : Form
{
    private TextBox txtServer;
    private Button btnTest, btnSave, btnCollect, btnOpen;
    private TextBox txtLog;
    private Label lblStatus;
    private string serverUrl = "";

    public CollectorForm()
    {
        Text = "计算机信息采集器";
        ClientSize = new Size(540, 580);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei", 9F);

        // --- 顶部：服务端地址配置区 ---
        var lblServer = new Label { Text = "服务端地址:", Location = new Point(20, 22), AutoSize = true };
        txtServer = new TextBox { Location = new Point(110, 18), Size = new Size(300, 23) };
        btnTest = new Button { Text = "测试连接", Location = new Point(420, 17), Size = new Size(100, 25) };
        btnTest.Click += (s, e) => TestConnection();

        btnSave = new Button { Text = "保存配置", Location = new Point(110, 50), Size = new Size(100, 25) };
        btnSave.Click += (s, e) => SaveConfig();
        var lblHint = new Label { Text = "保存到同目录 server.txt，下次启动自动读取", Location = new Point(220, 54), AutoSize = true, ForeColor = Color.Gray };

        // --- 状态栏 ---
        lblStatus = new Label
        {
            Text = "就绪",
            Location = new Point(20, 84),
            Size = new Size(500, 20),
            ForeColor = Color.DarkGreen,
            BackColor = Color.FromArgb(245, 245, 245),
            TextAlign = ContentAlignment.MiddleLeft
        };

        // --- 日志区 ---
        var lblLog = new Label { Text = "运行日志:", Location = new Point(20, 112), AutoSize = true };
        txtLog = new TextBox
        {
            Location = new Point(20, 132),
            Size = new Size(500, 360),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220)
        };

        // --- 底部按钮 ---
        btnCollect = new Button
        {
            Text = "开始采集",
            Location = new Point(160, 505),
            Size = new Size(160, 40),
            Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnCollect.FlatAppearance.BorderSize = 0;
        btnCollect.Click += (s, e) => StartCollect();

        btnOpen = new Button
        {
            Text = "打开登记页",
            Location = new Point(340, 510),
            Size = new Size(110, 30),
            Enabled = false
        };
        btnOpen.Click += (s, e) => OpenRegistrationPage();

        Controls.AddRange(new Control[] { lblServer, txtServer, btnTest, btnSave, lblHint, lblStatus, lblLog, txtLog, btnCollect, btnOpen });

        Load += (s, e) => LoadConfig();
        FormClosing += (s, e) => SaveConfigSilent();
    }

    // ---------------- 日志输出（线程安全） ----------------
    private void Log(string msg)
    {
        if (InvokeRequired)
            Invoke(new Action<string>(Log), msg);
        else
            txtLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + msg + Environment.NewLine);
    }

    private void SetStatus(string text, Color color)
    {
        if (InvokeRequired)
            Invoke(new Action<string, Color>(SetStatus), text, color);
        else
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }
    }

    private void SetButtons(bool enabled)
    {
        if (InvokeRequired)
            Invoke(new Action<bool>(SetButtons), enabled);
        else
        {
            btnCollect.Enabled = enabled;
            btnTest.Enabled = enabled;
            btnSave.Enabled = enabled;
        }
    }

    // ---------------- 配置读写 ----------------
    private string AppDir()
    {
        try
        {
            string p = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(p)) return Path.GetDirectoryName(p);
        }
        catch { }
        return Environment.CurrentDirectory;
    }

    private void LoadConfig()
    {
        try
        {
            string cfg = Path.Combine(AppDir(), "server.txt");
            if (File.Exists(cfg))
            {
                foreach (string raw in File.ReadAllLines(cfg, Encoding.UTF8))
                {
                    string t = raw == null ? "" : raw.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        serverUrl = t.TrimEnd('/');
                        txtServer.Text = serverUrl;
                        Log("已从 server.txt 读取服务端地址: " + serverUrl);
                        return;
                    }
                }
            }
        }
        catch { }
        Log("提示: 未找到 server.txt，请填写服务端地址并点保存配置。");
    }

    private void SaveConfig()
    {
        string url = txtServer.Text.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            MessageBox.Show("请先填写服务端地址", "提示");
            return;
        }
        try
        {
            string cfg = Path.Combine(AppDir(), "server.txt");
            File.WriteAllText(cfg, url + "\r\n# You can edit this line to change the server address.\r\n", Encoding.UTF8);
            serverUrl = url;
            Log("配置已保存到 " + cfg);
        }
        catch (Exception ex)
        {
            MessageBox.Show("保存失败: " + ex.Message, "错误");
        }
    }

    private void SaveConfigSilent()
    {
        try
        {
            string url = txtServer.Text.Trim().TrimEnd('/');
            if (url.Length == 0) return;
            string cfg = Path.Combine(AppDir(), "server.txt");
            if (!File.Exists(cfg))
            {
                File.WriteAllText(cfg, url + "\r\n", Encoding.UTF8);
            }
        }
        catch { }
    }

    // ---------------- 测试连接 ----------------
    private void TestConnection()
    {
        string url = txtServer.Text.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            MessageBox.Show("请先填写服务端地址", "提示");
            return;
        }
        SetButtons(false);
        SetStatus("测试中...", Color.DarkOrange);
        Log("正在测试连接 " + url + " ...");

        ThreadPool.QueueUserWorkItem(delegate(object state)
        {
            string svr = (string)state;
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(svr);
                req.Method = "GET";
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.AllowAutoRedirect = false;
                try
                {
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        int code = (int)resp.StatusCode;
                        if (code >= 500)
                        {
                            Log("[FAIL] 连接失败: 服务器返回 " + code + " " + resp.StatusCode + " (服务器或网关异常)");
                            SetStatus("连接失败", Color.Red);
                        }
                        else
                        {
                            Log("[OK] 连接成功! 服务器响应: " + code + " " + resp.StatusCode);
                            SetStatus("连接成功", Color.DarkGreen);
                        }
                    }
                }
                catch (WebException wex)
                {
                    if (wex.Response != null)
                    {
                        try
                        {
                            HttpWebResponse resp = (HttpWebResponse)wex.Response;
                            int code = (int)resp.StatusCode;
                            if (code >= 500)
                            {
                                Log("[FAIL] 连接失败: 服务器返回 " + code + " " + resp.StatusCode + " (服务器或网关异常,请确认服务端已启动)");
                                SetStatus("连接失败", Color.Red);
                            }
                            else
                            {
                                Log("[OK] 连接成功! 服务器响应: " + code + " " + resp.StatusCode + " (后台需登录,但服务器可达)");
                                SetStatus("连接成功", Color.DarkGreen);
                            }
                        }
                        catch
                        {
                            Log("[FAIL] 连接失败: 服务器返回异常响应");
                            SetStatus("连接失败", Color.Red);
                        }
                    }
                    else
                    {
                        Log("[FAIL] 连接失败: " + (wex.InnerException != null ? wex.InnerException.Message : wex.Message));
                        SetStatus("连接失败", Color.Red);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[FAIL] 连接失败: " + ex.Message);
                SetStatus("连接失败", Color.Red);
            }
            finally
            {
                SetButtons(true);
            }
        }, url);
    }

    // ---------------- 开始采集（后台线程） ----------------
    private string lastRecordId = "";

    private void StartCollect()
    {
        string url = txtServer.Text.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            MessageBox.Show("请先填写服务端地址", "提示");
            return;
        }
        serverUrl = url;
        SetButtons(false);
        btnOpen.Enabled = false;
        SetStatus("采集中...", Color.DarkOrange);
        txtLog.Clear();
        Log("==============================================");
        Log("服务端: " + serverUrl);
        Log("==============================================");

        Thread t = new Thread(delegate()
        {
            try
            {
                string id = DoCollectAndReport(serverUrl);
                lastRecordId = id;
                Log("");
                Log("[OK] 采集上报成功! 记录 ID: " + id);
                SetStatus("采集完成", Color.DarkGreen);
                SetButtons(true);
                btnOpen.Enabled = true;
                Log("点击下方『打开登记页』按钮填写使用人和部门。");
            }
            catch (Exception ex)
            {
                Log("");
                Log("[FAIL] 采集失败: " + ex.Message);
                Log("请检查: 1) 服务端地址是否正确  2) 服务端是否运行  3) 网络/防火墙");
                SetStatus("采集失败", Color.Red);
                SetButtons(true);
            }
        });
        t.IsBackground = true;
        t.Start();
    }

    private void OpenRegistrationPage()
    {
        if (lastRecordId.Length == 0)
        {
            MessageBox.Show("还没有采集记录，请先点击『开始采集』", "提示");
            return;
        }
        try
        {
            string url = serverUrl + "/?id=" + Uri.EscapeDataString(lastRecordId);
            System.Diagnostics.Process.Start(url);
            Log("已打开登记页: " + url);
        }
        catch (Exception ex)
        {
            Log("打开浏览器失败: " + ex.Message);
        }
    }

    // ==================== 采集逻辑（复用控制台版） ====================
    private string DoCollectAndReport(string serverUrl)
    {
        Log("[1/4] 采集系统信息...");

        ManagementObject cs = FirstWmi("SELECT Manufacturer, Model, PCSystemType, UserName, TotalPhysicalMemory FROM Win32_ComputerSystem");
        ManagementObject os = FirstWmi("SELECT Caption, Version, BuildNumber FROM Win32_OperatingSystem");
        ManagementObject bios = FirstWmi("SELECT SerialNumber FROM Win32_BIOS");
        ManagementObject cpu = FirstWmi("SELECT Name FROM Win32_Processor");

        string hostname = Environment.MachineName;
        string brand = Str(cs, "Manufacturer");
        string model = Str(cs, "Model");
        string serial = Str(bios, "SerialNumber");
        string loggedUser = Str(cs, "UserName");
        if (loggedUser.Length == 0) loggedUser = Environment.UserDomainName + "\\" + Environment.UserName;

        string deviceType = DetectDeviceType(cs);

        // ---- 内存 ----
        long memTotalBytes = 0;
        List<string> sticks = new List<string>();
        ManagementObjectCollection memColl = new ManagementObjectSearcher(
            "SELECT DeviceLocator, Manufacturer, PartNumber, Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory").Get();
        foreach (ManagementObject m in memColl)
        {
            long cap = ToLong(m["Capacity"]);
            memTotalBytes += cap;
            uint speed = ToUInt(m["ConfiguredClockSpeed"]);
            if (speed == 0) speed = ToUInt(m["Speed"]);
            sticks.Add("{"
                + "\"slot\":" + J(Str(m, "DeviceLocator")) + ","
                + "\"manufacturer\":" + J(Str(m, "Manufacturer")) + ","
                + "\"partno\":" + J(Str(m, "PartNumber")) + ","
                + "\"capacity_gb\":" + Math.Round(cap / 1073741824.0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"speed\":" + J(speed.ToString())
                + "}");
        }
        double memTotalGb = Math.Round(memTotalBytes / 1073741824.0, 2);

        // ---- 硬盘 ----
        List<string> disks = new List<string>();
        int diskCount = 0;
        ManagementObjectCollection diskColl = new ManagementObjectSearcher(
            "SELECT Model, InterfaceType, Size, SerialNumber FROM Win32_DiskDrive").Get();
        foreach (ManagementObject d in diskColl)
        {
            long size = ToLong(d["Size"]);
            diskCount++;
            disks.Add("{"
                + "\"model\":" + J(Str(d, "Model")) + ","
                + "\"interface\":" + J(Str(d, "InterfaceType")) + ","
                + "\"size_gb\":" + (size > 0 ? Math.Round(size / 1073741824.0, 0).ToString(System.Globalization.CultureInfo.InvariantCulture) : "null") + ","
                + "\"serial\":" + J(Str(d, "SerialNumber"))
                + "}");
        }

        // ---- 网络 ----
        List<string> nets = new List<string>();
        int netCount = 0;
        ManagementObjectCollection netColl = new ManagementObjectSearcher(
            "SELECT Description, MACAddress, IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE").Get();
        foreach (ManagementObject n in netColl)
        {
            string ipv4 = "";
            string[] ips = n["IPAddress"] as string[];
            if (ips != null)
            {
                List<string> v4 = new List<string>();
                foreach (string ip in ips)
                {
                    if (IsIPv4(ip)) v4.Add(ip);
                }
                ipv4 = string.Join(", ", v4.ToArray());
            }
            netCount++;
            nets.Add("{"
                + "\"name\":" + J(Str(n, "Description")) + ","
                + "\"mac\":" + J(Str(n, "MACAddress")) + ","
                + "\"ip\":" + J(ipv4)
                + "}");
        }

        // ---- 软件 ----
        Log("[2/4] 采集已安装软件列表...");
        List<string[]> software = ReadSoftware();

        Log("[3/4] 发现: " + sticks.Count + " 条内存, " + diskCount + " 块硬盘, "
            + netCount + " 个网卡, " + software.Count + " 项软件。");

        // ---- 构造 JSON ----
        StringBuilder sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"hostname\":").Append(J(hostname)).Append(",");
        sb.Append("\"brand\":").Append(J(brand)).Append(",");
        sb.Append("\"model\":").Append(J(model)).Append(",");
        sb.Append("\"device_type\":").Append(J(deviceType)).Append(",");
        sb.Append("\"serial\":").Append(J(serial)).Append(",");
        sb.Append("\"os_version\":").Append(J(Str(os, "Caption"))).Append(",");
        sb.Append("\"os_build\":").Append(J(Str(os, "Version") + " (Build " + Str(os, "BuildNumber") + ")")).Append(",");
        sb.Append("\"cpu\":").Append(J(Str(cpu, "Name"))).Append(",");
        sb.Append("\"logged_user\":").Append(J(loggedUser)).Append(",");
        sb.Append("\"memory_total_gb\":").Append(J(memTotalGb.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append(",");
        sb.Append("\"memory\":[").Append(string.Join(",", sticks.ToArray())).Append("],");
        sb.Append("\"disks\":[").Append(string.Join(",", disks.ToArray())).Append("],");
        sb.Append("\"network\":[").Append(string.Join(",", nets.ToArray())).Append("],");
        StringBuilder swJson = new StringBuilder();
        for (int i = 0; i < software.Count; i++)
        {
            if (i > 0) swJson.Append(",");
            swJson.Append("{\"name\":").Append(J(software[i][0]))
                  .Append(",\"version\":").Append(J(software[i][1]))
                  .Append(",\"publisher\":").Append(J(software[i][2])).Append("}");
        }
        sb.Append("\"software\":[").Append(swJson.ToString()).Append("]");
        sb.Append("}");

        Log("[4/4] 上报到服务器 " + serverUrl + " ...");
        string resp = HttpPost(serverUrl + "/api/report", sb.ToString());

        string id = ExtractJsonString(resp, "id");
        if (resp.IndexOf("\"ok\":true") >= 0 && id.Length > 0)
        {
            return id;
        }
        throw new Exception("服务器返回异常: " + resp);
    }

    // ---------------- WMI helpers ----------------
    private static ManagementObject FirstWmi(string query)
    {
        try
        {
            foreach (ManagementObject o in new ManagementObjectSearcher(query).Get()) return o;
        }
        catch { }
        return null;
    }

    private static string Str(ManagementObject mo, string prop)
    {
        if (mo == null) return "";
        try
        {
            object v = mo[prop];
            if (v == null) return "";
            return v.ToString().Trim();
        }
        catch { return ""; }
    }

    private static long ToLong(object v)
    {
        if (v == null) return 0;
        try { return Convert.ToInt64(v); } catch { return 0; }
    }

    private static uint ToUInt(object v)
    {
        if (v == null) return 0;
        try { return Convert.ToUInt32(v); } catch { return 0; }
    }

    private static bool IsIPv4(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        string[] p = s.Split('.');
        if (p.Length != 4) return false;
        int n;
        foreach (string part in p)
        {
            if (part.Length == 0 || part.Length > 3) return false;
            if (!int.TryParse(part, out n) || n < 0 || n > 255) return false;
        }
        return true;
    }

    private static string DetectDeviceType(ManagementObject cs)
    {
        try
        {
            int[] laptopTypes = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
            int pst = -1;
            if (cs != null && cs["PCSystemType"] != null) pst = Convert.ToInt32(cs["PCSystemType"]);

            List<int> chassis = new List<int>();
            foreach (ManagementObject enc in new ManagementObjectSearcher("SELECT ChassisTypes FROM Win32_SystemEnclosure").Get())
            {
                ushort[] cts = enc["ChassisTypes"] as ushort[];
                if (cts != null) foreach (ushort c in cts) chassis.Add((int)c);
            }

            if (pst == 2) return "Laptop";
            bool chassisLaptop = false;
            foreach (int c in chassis) foreach (int t in laptopTypes) if (c == t) chassisLaptop = true;
            if (pst == 1 && chassisLaptop) return "Laptop";
            if (pst == -1 && chassisLaptop) return "Laptop";
            return "Desktop";
        }
        catch { return "Desktop"; }
    }

    // ---------------- Software (registry) ----------------
    private static List<string[]> ReadSoftware()
    {
        List<string[]> list = new List<string[]>();
        HashSet<string> seen = new HashSet<string>();
        string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, sub, list, seen);
        ReadUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, sub, list, seen);
        ReadUninstall(RegistryHive.CurrentUser, RegistryView.Default, sub, list, seen);
        list.Sort(delegate(string[] a, string[] b)
        {
            int c = string.Compare(a[0], b[0], StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
            return string.Compare(a[1], b[1], StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    private static void ReadUninstall(RegistryHive hive, RegistryView view, string sub,
        List<string[]> list, HashSet<string> seen)
    {
        try
        {
            using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view))
            using (RegistryKey un = root.OpenSubKey(sub))
            {
                if (un == null) return;
                foreach (string name in un.GetSubKeyNames())
                {
                    try
                    {
                        using (RegistryKey k = un.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            string dn = k.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(dn)) continue;
                            object sc = k.GetValue("SystemComponent");
                            if (sc != null)
                            {
                                try { if (Convert.ToInt32(sc) == 1) continue; } catch { }
                            }
                            if (dn.StartsWith("KB", StringComparison.OrdinalIgnoreCase)) continue;
                            string ver = k.GetValue("DisplayVersion") as string;
                            string pub = k.GetValue("Publisher") as string;
                            if (ver == null) ver = "";
                            if (pub == null) pub = "";
                            string key = dn.Trim().ToLowerInvariant() + "|" + ver.Trim().ToLowerInvariant();
                            if (seen.Contains(key)) continue;
                            seen.Add(key);
                            list.Add(new string[] { dn.Trim(), ver.Trim(), pub.Trim() });
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    // ---------------- JSON / HTTP ----------------
    private static string J(string s)
    {
        if (s == null) s = "";
        StringBuilder sb = new StringBuilder("\"", s.Length + 2);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(Convert.ToInt32(c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append("\"");
        return sb.ToString();
    }

    private static string HttpPost(string url, string json)
    {
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.ContentType = "application/json; charset=utf-8";
        req.Timeout = 30000;
        req.ReadWriteTimeout = 30000;
        byte[] data = Encoding.UTF8.GetBytes(json);
        req.ContentLength = data.Length;
        using (Stream rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
        using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
        using (Stream st = resp.GetResponseStream())
        using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
            return sr.ReadToEnd();
    }

    private static string ExtractJsonString(string json, string key)
    {
        string pat = "\"" + key + "\":\"";
        int i = json.IndexOf(pat, StringComparison.Ordinal);
        if (i < 0) return "";
        int start = i + pat.Length;
        StringBuilder sb = new StringBuilder();
        for (int p = start; p < json.Length; p++)
        {
            char c = json[p];
            if (c == '\\' && p + 1 < json.Length) { sb.Append(json[p + 1]); p++; continue; }
            if (c == '"') break;
            sb.Append(c);
        }
        return sb.ToString();
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 带参数时跳过 GUI（保留控制台兼容，详见 collect.exe）
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CollectorForm());
    }
}
