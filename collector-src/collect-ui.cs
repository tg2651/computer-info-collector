// ==============================================================
//  Computer Info Collector - WinForms GUI edition (Flat Design)
//  Features:
//    1) Configure the server URL (saved to server.txt)
//    2) Test connectivity to the server
//    3) Collect hardware/OS/software info and report to server
//    4) Open the registration page in the default browser
//
//  Visual style: flat design with rounded corners, card layout,
//  minimal visual noise, consistent color palette.
//
//  Requires: .NET Framework 4.0+ (preinstalled on Win7+)
//  Build:
//    csc /nologo /platform:anycpu /r:System.Management.dll ^
//        /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
//        /out:collect-ui.exe collect-ui.cs
// ==============================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// ==================== 主题色彩 ====================
internal static class Theme
{
    public static readonly Color WindowBg = ColorTranslator.FromHtml("#F0F2F5");
    public static readonly Color Card = Color.White;
    public static readonly Color Primary = ColorTranslator.FromHtml("#0078D4");
    public static readonly Color PrimaryHover = ColorTranslator.FromHtml("#106EBE");
    public static readonly Color PrimaryDown = ColorTranslator.FromHtml("#005A9E");
    public static readonly Color PrimaryLight = ColorTranslator.FromHtml("#C7E0F4");
    public static readonly Color SecondaryBg = Color.White;
    public static readonly Color SecondaryBorder = ColorTranslator.FromHtml("#0078D4");
    public static readonly Color Text = ColorTranslator.FromHtml("#1F2937");
    public static readonly Color TextSecondary = ColorTranslator.FromHtml("#6B7280");
    public static readonly Color TextHint = ColorTranslator.FromHtml("#9CA3AF");
    public static readonly Color Border = ColorTranslator.FromHtml("#D1D5DB");
    public static readonly Color BorderFocus = ColorTranslator.FromHtml("#0078D4");
    public static readonly Color Divider = ColorTranslator.FromHtml("#E5E7EB");
    public static readonly Color LogBg = ColorTranslator.FromHtml("#1E1E1E");
    public static readonly Color LogText = ColorTranslator.FromHtml("#D4D4D4");
    public static readonly Color LogDim = ColorTranslator.FromHtml("#6A9955");
    public static readonly Color Success = ColorTranslator.FromHtml("#10B981");
    public static readonly Color Error = ColorTranslator.FromHtml("#EF4444");
    public static readonly Color Warning = ColorTranslator.FromHtml("#F59E0B");
    public static readonly Color Idle = ColorTranslator.FromHtml("#6B7280");
}

// ==================== 圆角工具 ====================
internal static class UI
{
    public static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        int d = radius * 2;
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        radius = d / 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

// ==================== 圆角扁平按钮 ====================
internal class FlatButton : Button
{
    public int Radius { get; set; }
    public bool IsSecondary { get; set; }
    private bool hovering = false;
    private bool pressing = false;

    public FlatButton()
    {
        Radius = 6;
        IsSecondary = false;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering = false; pressing = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressing = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressing = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = UI.RoundRect(rect, Radius))
        {
            if (IsSecondary)
            {
                // 次按钮: 白底 + 蓝边 + 蓝字
                Color bg = Enabled ? (pressing ? Color.FromArgb(0xF3, 0xF9, 0xFF) : (hovering ? Color.FromArgb(0xEB, 0xF5, 0xFF) : Color.White)) : Color.FromArgb(0xF3, 0xF4, 0xF6);
                Color bd = Enabled ? (hovering ? Theme.PrimaryDown : Theme.SecondaryBorder) : Color.FromArgb(0xD1, 0xD5, 0xDB);
                Color tx = Enabled ? (hovering ? Theme.PrimaryDown : Theme.Primary) : Color.FromArgb(0x9C, 0xA3, 0xAF);
                using (Brush b = new SolidBrush(bg)) g.FillPath(b, path);
                using (Pen p = new Pen(bd, 1.5f)) g.DrawPath(p, path);
                TextRenderer.DrawText(g, Text, Font, rect, tx, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                // 主按钮: 实色填充 + 白字
                Color c = Theme.Primary;
                if (!Enabled) c = Color.FromArgb(0xBD, 0xE4, 0xF8);
                else if (pressing) c = Theme.PrimaryDown;
                else if (hovering) c = Theme.PrimaryHover;
                using (Brush b = new SolidBrush(c)) g.FillPath(b, path);
                TextRenderer.DrawText(g, Text, Font, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}

// ==================== 圆角面板 ====================
internal class RoundedPanel : Panel
{
    public int Radius { get; set; }
    public Color BorderColor { get; set; }
    public new Color BackColor { get { return base.BackColor; } set { base.BackColor = value; } }

    public RoundedPanel()
    {
        Radius = 8;
        BorderColor = Color.Empty;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Radius > 0)
        {
            try { this.Region = new Region(UI.RoundRect(new Rectangle(0, 0, Width, Height), Radius)); }
            catch { }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = UI.RoundRect(rect, Radius))
        {
            using (Brush b = new SolidBrush(base.BackColor)) g.FillPath(b, path);
            if (BorderColor != Color.Empty)
            {
                using (Pen p = new Pen(BorderColor, 1)) g.DrawPath(p, path);
            }
        }
    }
}

// ==================== 圆角输入框容器 ====================
internal class RoundedInput : RoundedPanel
{
    public TextBox Inner { get; private set; }
    private bool focused = false;

    public RoundedInput()
    {
        Radius = 6;
        base.BackColor = Color.White;
        BorderColor = Theme.Border;
        Inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Location = new Point(10, 8),
            Font = new Font("Microsoft YaHei", 9.5F),
            BackColor = Color.White,
            ForeColor = Theme.Text
        };
        Inner.GotFocus += (s, e) => { focused = true; BorderColor = Theme.BorderFocus; Invalidate(); };
        Inner.LostFocus += (s, e) => { focused = false; BorderColor = Theme.Border; Invalidate(); };
        Controls.Add(Inner);
        Resize += (s, e) => { Inner.Width = Width - 20; Inner.Height = Height - 16; };
    }

    public string TextValue
    {
        get { return Inner.Text; }
        set { Inner.Text = value; }
    }
}

// ==================== 主窗体 ====================
internal class CollectorForm : Form
{
    private RoundedInput txtServer;
    private FlatButton btnTest, btnSave, btnCollect, btnOpen;
    private TextBox txtLog;
    private Label lblStatus;
    private Panel statusDot;
    private string serverUrl = "";

    public CollectorForm()
    {
        Text = "计算机信息采集器";
        ClientSize = new Size(540, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Microsoft YaHei", 9F);
        BackColor = Theme.WindowBg;

        // ====== 卡片容器 ======
        RoundedPanel card = new RoundedPanel
        {
            Location = new Point(16, 16),
            Size = new Size(508, 588),
            Radius = 12,
            BackColor = Theme.Card
        };
        Controls.Add(card);

        // ====== 标题 ======
        var lblTitle = new Label
        {
            Text = "计算机信息采集器",
            Location = new Point(28, 24),
            Size = new Size(300, 26),
            Font = new Font("Microsoft YaHei", 14F, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Theme.Card
        };
        card.Controls.Add(lblTitle);

        var lblSubtitle = new Label
        {
            Text = "配置服务端地址并采集本机信息",
            Location = new Point(28, 52),
            Size = new Size(300, 18),
            Font = new Font("Microsoft YaHei", 9F),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Card
        };
        card.Controls.Add(lblSubtitle);

        // ====== 分隔线 ======
        var divider = new Panel
        {
            Location = new Point(28, 80),
            Size = new Size(452, 1),
            BackColor = Theme.Divider
        };
        card.Controls.Add(divider);

        // ====== 服务端地址 ======
        var lblServer = new Label
        {
            Text = "服务端地址",
            Location = new Point(28, 94),
            Size = new Size(200, 18),
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Theme.Card
        };
        card.Controls.Add(lblServer);

        txtServer = new RoundedInput
        {
            Location = new Point(28, 116),
            Size = new Size(352, 34)
        };
        card.Controls.Add(txtServer);

        btnTest = new FlatButton
        {
            Text = "测试连接",
            Location = new Point(388, 116),
            Size = new Size(100, 34),
            Font = new Font("Microsoft YaHei", 9F),
            IsSecondary = true
        };
        btnTest.Click += (s, e) => TestConnection();
        card.Controls.Add(btnTest);

        btnSave = new FlatButton
        {
            Text = "保存配置",
            Location = new Point(28, 158),
            Size = new Size(110, 30),
            Font = new Font("Microsoft YaHei", 9F),
            IsSecondary = true
        };
        btnSave.Click += (s, e) => SaveConfig();
        card.Controls.Add(btnSave);

        var lblHint = new Label
        {
            Text = "保存到同目录 server.txt，下次自动读取",
            Location = new Point(146, 164),
            Size = new Size(300, 18),
            Font = new Font("Microsoft YaHei", 8.5F),
            ForeColor = Theme.TextHint,
            BackColor = Theme.Card
        };
        card.Controls.Add(lblHint);

        // ====== 状态栏（圆点+文字） ======
        statusDot = new Panel
        {
            Location = new Point(28, 198),
            Size = new Size(8, 8),
            BackColor = Theme.Idle
        };
        MakeDotRound(statusDot);
        card.Controls.Add(statusDot);

        lblStatus = new Label
        {
            Text = "就绪",
            Location = new Point(42, 196),
            Size = new Size(430, 18),
            Font = new Font("Microsoft YaHei", 9F),
            ForeColor = Theme.TextSecondary,
            BackColor = Theme.Card,
            TextAlign = ContentAlignment.MiddleLeft
        };
        card.Controls.Add(lblStatus);

        // ====== 运行日志 ======
        var lblLog = new Label
        {
            Text = "运行日志",
            Location = new Point(28, 224),
            Size = new Size(200, 18),
            Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
            ForeColor = Theme.Text,
            BackColor = Theme.Card
        };
        card.Controls.Add(lblLog);

        // 日志区容器（圆角深色）
        RoundedPanel logPanel = new RoundedPanel
        {
            Location = new Point(28, 246),
            Size = new Size(452, 196),
            Radius = 6,
            BackColor = Theme.LogBg
        };
        card.Controls.Add(logPanel);

        txtLog = new TextBox
        {
            Location = new Point(10, 8),
            Size = new Size(432, 180),
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Theme.LogBg,
            ForeColor = Theme.LogText
        };
        logPanel.Controls.Add(txtLog);

        // ====== 主按钮 ======
        btnCollect = new FlatButton
        {
            Text = "开始采集",
            Location = new Point(134, 460),
            Size = new Size(240, 42),
            Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
            IsSecondary = false
        };
        btnCollect.Click += (s, e) => StartCollect();
        card.Controls.Add(btnCollect);

        btnOpen = new FlatButton
        {
            Text = "打开登记页",
            Location = new Point(174, 512),
            Size = new Size(160, 30),
            Font = new Font("Microsoft YaHei", 9F),
            IsSecondary = true,
            Enabled = false
        };
        btnOpen.Click += (s, e) => OpenRegistrationPage();
        card.Controls.Add(btnOpen);

        // ====== 事件 ======
        Load += (s, e) => LoadConfig();
        FormClosing += (s, e) => SaveConfigSilent();
    }

    private void MakeDotRound(Panel dot)
    {
        dot.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Brush b = new SolidBrush(dot.BackColor))
                e.Graphics.FillEllipse(b, 0, 0, dot.Width - 1, dot.Height - 1);
        };
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
            statusDot.BackColor = color;
            statusDot.Invalidate();
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
                        txtServer.TextValue = serverUrl;
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
        string url = txtServer.TextValue.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            MessageBox.Show("请先填写服务端地址", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show("保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveConfigSilent()
    {
        try
        {
            string url = txtServer.TextValue.Trim().TrimEnd('/');
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
        string url = txtServer.TextValue.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            MessageBox.Show("请先填写服务端地址", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SetButtons(false);
        SetStatus("测试中...", Theme.Warning);
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
                            SetStatus("连接失败", Theme.Error);
                        }
                        else
                        {
                            Log("[OK] 连接成功! 服务器响应: " + code + " " + resp.StatusCode);
                            SetStatus("连接成功", Theme.Success);
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
                                SetStatus("连接失败", Theme.Error);
                            }
                            else
                            {
                                Log("[OK] 连接成功! 服务器响应: " + code + " " + resp.StatusCode + " (后台需登录,但服务器可达)");
                                SetStatus("连接成功", Theme.Success);
                            }
                        }
                        catch
                        {
                            Log("[FAIL] 连接失败: 服务器返回异常响应");
                            SetStatus("连接失败", Theme.Error);
                        }
                    }
                    else
                    {
                        Log("[FAIL] 连接失败: " + (wex.InnerException != null ? wex.InnerException.Message : wex.Message));
                        SetStatus("连接失败", Theme.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("[FAIL] 连接失败: " + ex.Message);
                SetStatus("连接失败", Theme.Error);
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
        string url = txtServer.TextValue.Trim().TrimEnd('/');
        if (url.Length == 0)
        {
            MessageBox.Show("请先填写服务端地址", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        serverUrl = url;
        SetButtons(false);
        btnOpen.Enabled = false;
        SetStatus("采集中...", Theme.Warning);
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
                SetStatus("采集完成", Theme.Success);
                SetButtons(true);
                btnOpen.Enabled = true;
                Log("点击下方『打开登记页』按钮填写使用人和部门。");
            }
            catch (Exception ex)
            {
                Log("");
                Log("[FAIL] 采集失败: " + ex.Message);
                Log("请检查: 1) 服务端地址是否正确  2) 服务端是否运行  3) 网络/防火墙");
                SetStatus("采集失败", Theme.Error);
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
            MessageBox.Show("还没有采集记录，请先点击『开始采集』", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    // ==================== 采集逻辑 ====================
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
                foreach (string ip in ips) { if (IsIPv4(ip)) v4.Add(ip); }
                ipv4 = string.Join(", ", v4.ToArray());
            }
            netCount++;
            nets.Add("{"
                + "\"name\":" + J(Str(n, "Description")) + ","
                + "\"mac\":" + J(Str(n, "MACAddress")) + ","
                + "\"ip\":" + J(ipv4)
                + "}");
        }

        Log("[2/4] 采集已安装软件列表...");
        List<string[]> software = ReadSoftware();

        Log("[3/4] 发现: " + sticks.Count + " 条内存, " + diskCount + " 块硬盘, "
            + netCount + " 个网卡, " + software.Count + " 项软件。");

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
        if (resp.IndexOf("\"ok\":true") >= 0 && id.Length > 0) return id;
        throw new Exception("服务器返回异常: " + resp);
    }

    // ---------------- WMI helpers ----------------
    private static ManagementObject FirstWmi(string query)
    {
        try { foreach (ManagementObject o in new ManagementObjectSearcher(query).Get()) return o; } catch { }
        return null;
    }
    private static string Str(ManagementObject mo, string prop)
    {
        if (mo == null) return "";
        try { object v = mo[prop]; return v == null ? "" : v.ToString().Trim(); } catch { return ""; }
    }
    private static long ToLong(object v) { if (v == null) return 0; try { return Convert.ToInt64(v); } catch { return 0; } }
    private static uint ToUInt(object v) { if (v == null) return 0; try { return Convert.ToUInt32(v); } catch { return 0; } }
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
    private static void ReadUninstall(RegistryHive hive, RegistryView view, string sub, List<string[]> list, HashSet<string> seen)
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
                            if (sc != null) { try { if (Convert.ToInt32(sc) == 1) continue; } catch { } }
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

// ==================== 入口 ====================
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CollectorForm());
    }
}
