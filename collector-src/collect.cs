// ==============================================================
//  Computer Info Collector (.NET edition)
//  Single-file collector for Windows where PowerShell is blocked.
//  Requires: .NET Framework 4.0+ (preinstalled on Win7+)
//
//  Server URL configuration (priority order):
//    1) Command-line argument:  collect.exe http://192.168.1.100
//    2) server.txt next to collect.exe (first non-empty, non-# line)
//    3) First run prompts interactively and saves to server.txt
//
//  Other flags:
//    -NoOpen    Do not open the browser after reporting
//    -NoPause   Do not wait for Enter at the end
//
//  Build:
//    csc /nologo /platform:anycpu /r:System.Management.dll /out:collect.exe collect.cs
// ==============================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Text;
using Microsoft.Win32;

internal class Collector
{
    private static string ServerUrl = "";
    private static bool NoPause = false;
    private static bool NoOpen = false;

    private static int Main(string[] args)
    {
        foreach (string a in args)
        {
            string x = a.Trim();
            string low = x.ToLowerInvariant();
            if (low == "-nopause" || low == "--nopause") NoPause = true;
            else if (low == "-noopen" || low == "--noopen") NoOpen = true;
        }

        ServerUrl = ResolveServerUrl(args);
        if (ServerUrl.Length == 0)
        {
            Console.WriteLine("FAILED: No server address provided.");
            Console.WriteLine("Usage:");
            Console.WriteLine("  collect.exe http://your-server");
            Console.WriteLine("  collect.exe http://192.168.1.100:3000");
            Console.WriteLine("Or place the URL in a server.txt file next to collect.exe.");
            if (!NoPause) { Console.WriteLine("Press Enter to exit..."); try { Console.ReadLine(); } catch { } }
            return 1;
        }

        Console.WriteLine("==============================================");
        Console.WriteLine(" Computer Info Collector");
        Console.WriteLine(" Server: " + ServerUrl);
        Console.WriteLine("==============================================");
        try
        {
            Console.WriteLine("[1/4] Collecting system information...");

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

            // ---- Memory ----
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

            // ---- Disks ----
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

            // ---- Network ----
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

            // ---- Software ----
            Console.WriteLine("[2/4] Collecting installed software list...");
            List<string[]> software = ReadSoftware();

            Console.WriteLine("[3/4] Found: " + sticks.Count + " memory stick(s), " + diskCount
                + " disk(s), " + netCount + " network adapter(s), " + software.Count + " software item(s).");

            // ---- Build JSON ----
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

            Console.WriteLine("[4/4] Reporting to " + ServerUrl + " ...");
            string resp = HttpPost(ServerUrl + "/api/report", sb.ToString());

            string id = ExtractJsonString(resp, "id");
            if (resp.IndexOf("\"ok\":true") >= 0 && id.Length > 0)
            {
                string url = ServerUrl + "/?id=" + Uri.EscapeDataString(id);
                Console.WriteLine("");
                Console.WriteLine("SUCCESS! Record id: " + id);
                Console.WriteLine("Opening registration page: " + url);
                if (!NoOpen)
                {
                    try { Process.Start(url); }
                    catch (Exception e) { Console.WriteLine("Open browser failed: " + e.Message); }
                }
            }
            else
            {
                throw new Exception("Unexpected server response: " + resp);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("");
            Console.WriteLine("FAILED: " + ex.Message);
            Console.WriteLine("Please check: 1) server address is correct  2) server is running  3) network/firewall allows this connection.");
            if (!NoPause) { Console.WriteLine("Press Enter to exit..."); try { Console.ReadLine(); } catch { } }
            return 1;
        }

        if (!NoPause) { Console.WriteLine("Press Enter to exit..."); try { Console.ReadLine(); } catch { } }
        return 0;
    }

    // ---------------- Server URL resolution ----------------
    // Priority: 1) command-line arg  2) server.txt next to exe  3) interactive prompt (saved)
    private static string ResolveServerUrl(string[] args)
    {
        // 1) Command-line argument
        foreach (string a in args)
        {
            string x = a == null ? "" : a.Trim();
            if (x.Length == 0 || x.StartsWith("-")) continue;
            if (x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                x.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return x.TrimEnd('/');
            }
        }

        // 2) server.txt next to the exe
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
                        return t.TrimEnd('/');
                    }
                }
            }
        }
        catch { }

        // 3) Interactive prompt
        Console.WriteLine("Server address is not configured.");
        Console.WriteLine("Enter the server URL (e.g. http://192.168.1.100  or  http://your-server:3000).");
        Console.WriteLine("This will be saved to server.txt so you do not need to type it again.");
        Console.Write("> ");
        string input;
        try { input = Console.ReadLine(); } catch { input = null; }
        if (input == null) return "";
        input = input.Trim().TrimEnd('/');
        if (input.Length == 0) return "";
        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            input = "http://" + input;
        }

        // Save for next time
        try
        {
            string cfg = Path.Combine(AppDir(), "server.txt");
            File.WriteAllText(cfg, input + "\r\n# You can edit this line to change the server address.\r\n", Encoding.UTF8);
            Console.WriteLine("Saved to " + cfg);
            Console.WriteLine();
        }
        catch { }

        return input;
    }

    private static string AppDir()
    {
        try
        {
            string p = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(p)) return Path.GetDirectoryName(p);
        }
        catch { }
        return Environment.CurrentDirectory;
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
