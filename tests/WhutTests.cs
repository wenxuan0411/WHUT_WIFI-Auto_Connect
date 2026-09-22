using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

static class WhutTests
{
    static int count;
    static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        Console.WriteLine("PASS " + description);
        count++;
    }

    static void MustFail(Action action, string description)
    {
        bool failed = false;
        try { action(); } catch { failed = true; }
        Check(failed, description);
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args[0] == "--wifi-live")
            {
                using (var wifi = new NativeWifi())
                {
                    var current = wifi.GetNetworks(false);
                    Check(current.Any(n => n.Connected && CampusWifi.IsTarget(n.Ssid)), "live already connected to campus Wi-Fi");
                    var visible = wifi.GetNetworks(true);
                    Check(visible.Any(n => CampusWifi.IsTarget(n.Ssid)), "native scan discovers campus SSID");
                    Check(CampusWifi.EnsureConnected(wifi, System.Threading.Thread.Sleep) == null, "keep existing campus association");
                }
                return 0;
            }
            if (args[0] == "--live")
            {
                var portal = PortalClient.DiscoverPortal(PortalSettings.WhutTemplate().PortalUrl);
                Check(portal.ApiUrl.AbsoluteUri == "http://172.30.21.100/api/", "live WHUT API discovery");
                Check(portal.Parameters.ContainsKey("nasId"), "live NAS parameter available");
                Check(portal.Parameters.ContainsKey("ip") || portal.Parameters.ContainsKey("wlanuserip"), "live client IP available");
                Check(!String.IsNullOrEmpty(portal.CsrfToken), "live CSRF token available (not printed)");
                Console.WriteLine("Internet currently available: " + PortalClient.HasInternet(PortalSettings.WhutTemplate()));
                Console.WriteLine("No credentials loaded; no authentication POST sent.");
                return 0;
            }
            // 测试进程使用独立目录，不能读取或改写用户已保存的凭据和日志。
            string testData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test-data-" + Guid.NewGuid().ToString("N"));
            foreach (string name in new[] { "DirectoryPath", "SettingsPath", "CredentialPath", "LogPath" })
            {
                string path = name == "DirectoryPath" ? testData : Path.Combine(testData, name);
                typeof(AppData).GetField(name, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, path);
            }
            TestWifi();
            string root = args[0];
            var context = PortalClient.DiscoverPortal(root + "/ok/start");
            Check(context.ApiUrl.AbsoluteUri == root + "/ok/api/", "ignore commented host_url and preserve /api prefix");
            Check(context.Parameters["nasId"] == "14", "follow JavaScript and HTTP redirects");
            Check(PortalClient.SubmitLogin(context, "test+user", "p&=+% 密码") == null, "encoded credentials, cookies, CSRF and ip mapping");

            foreach (string scenario in new[] { "wlan", "legacy", "stringcode" })
                Check(PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/" + scenario + "/start"), "test+user", "p&=+% 密码") == null, scenario);
            foreach (string scenario in new[] { "denied", "captcha", "unknown", "falsezero" })
                Check(PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/" + scenario + "/start"), "test+user", "p&=+% 密码") != null, "reject " + scenario);
            foreach (string scenario in new[] { "badjson", "postredirect" })
                MustFail(delegate { PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/" + scenario + "/start"), "test+user", "p&=+% 密码"); }, "reject " + scenario);
            foreach (string scenario in new[] { "loop", "crossorigin", "crossapi", "csrfmissing", "csrfempty", "noconfig" })
                MustFail(delegate { PortalClient.DiscoverPortal(root + "/" + scenario + "/start"); }, "reject " + scenario);
            MustFail(delegate { PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/missingparams/start"), "test+user", "p&=+% 密码"); }, "no POST without terminal parameters");

            var settings = PortalSettings.WhutTemplate();
            settings.ConnectivityUrl = root + "/probe/ok";
            Check(PortalClient.HasInternet(settings), "exact connectivity response");
            foreach (string path in new[] { "portal", "redirect", "error", "empty" })
            {
                settings.ConnectivityUrl = root + "/probe/" + path;
                Check(!PortalClient.HasInternet(settings), "reject connectivity " + path);
            }
            settings.ConnectivityUrl = root + "/probe/ok";
            settings.ConnectivityExpected = "";
            Check(!PortalClient.HasInternet(settings), "empty expected response never means online");
            Check(AppData.AppName == "CampusFlow-WHUT", "separate settings from upstream app");

            Application.EnableVisualStyles();
            using (var form = new MainForm())
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                Application.DoEvents();
                form.DrawToBitmap(bitmap, form.ClientRectangle);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whut-ui.png"));
                var field = typeof(MainForm).GetField("portalUrl", BindingFlags.Instance | BindingFlags.NonPublic);
                Check(((TextBox)field.GetValue(form)).Text == PortalSettings.WhutTemplate().PortalUrl, "UI loads WHUT default");
                form.Close();
            }
            Console.WriteLine("Passed " + count + " checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    sealed class FakeWifi : IWifiController
    {
        internal List<WifiNetwork> Networks = new List<WifiNetwork>();
        internal List<string> Attempts = new List<string>();
        internal int Scans;
        internal bool FailDorm, NeverAssociate, RejectRequest, ConnectDuringScan;
        public List<WifiNetwork> GetNetworks(bool scan)
        {
            if (scan)
            {
                Scans++;
                if (ConnectDuringScan) Networks.First(n => CampusWifi.IsTarget(n.Ssid)).Connected = true;
            }
            return Networks;
        }
        public bool Connect(WifiNetwork target)
        {
            Attempts.Add(target.Ssid);
            if (RejectRequest) return false;
            if (!NeverAssociate && !(FailDorm && target.Ssid == "WHUT-DORM"))
            {
                foreach (var n in Networks) n.Connected = false;
                target.Connected = true;
            }
            return true;
        }
    }

    static WifiNetwork Network(string ssid, bool connected = false)
    {
        return new WifiNetwork { Ssid = ssid, Connected = connected, Connectable = true, Signal = 80 };
    }

    static void TestWifi()
    {
        Action<int> wait = delegate { };
        var wifi = new FakeWifi();
        wifi.Networks.Add(Network("WHUT-WLAN", true));
        Check(CampusWifi.EnsureConnected(wifi, wait) == null && wifi.Scans == 0 && wifi.Attempts.Count == 0, "keep existing target without scanning or reconnecting");
        wifi = new FakeWifi();
        wifi.Networks.Add(Network("Other", true));
        wifi.Networks.Add(Network("WHUT-WLAN"));
        wifi.Networks.Add(Network("WHUT-DORM"));
        Check(CampusWifi.EnsureConnected(wifi, wait) == null && wifi.Attempts.SequenceEqual(new[] { "WHUT-DORM" }), "scan and switch from other network, prefer DORM");
        wifi = new FakeWifi { FailDorm = true };
        wifi.Networks.Add(Network("WHUT-DORM"));
        wifi.Networks.Add(Network("WHUT-WLAN"));
        Check(CampusWifi.EnsureConnected(wifi, wait) == null && wifi.Attempts.SequenceEqual(new[] { "WHUT-DORM", "WHUT-WLAN" }), "fallback to WLAN after DORM association timeout");
        wifi = new FakeWifi();
        wifi.Networks.Add(Network("Other", true));
        wifi.Networks.Add(Network("WHUT-DORM-FAKE"));
        Check(CampusWifi.EnsureConnected(wifi, wait) != null && wifi.Attempts.Count == 0 && wifi.Networks[0].Connected, "no target leaves current network untouched; exact SSID match");
        wifi = new FakeWifi { NeverAssociate = true };
        wifi.Networks.Add(Network("WHUT-DORM"));
        Check(CampusWifi.EnsureConnected(wifi, wait) != null, "accepted request alone does not prove association");
        wifi = new FakeWifi { RejectRequest = true };
        wifi.Networks.Add(Network("WHUT-WLAN"));
        Check(CampusWifi.EnsureConnected(wifi, wait) != null, "connection request rejection");
        wifi = new FakeWifi { ConnectDuringScan = true };
        wifi.Networks.Add(Network("WHUT-WLAN"));
        Check(CampusWifi.EnsureConnected(wifi, wait) == null && wifi.Attempts.Count == 0, "Windows connects during scan; skip duplicate connect");
        wifi = new FakeWifi();
        var secure = Network("WHUT-WLAN");
        secure.Secure = true;
        wifi.Networks.Add(secure);
        Check(CampusWifi.EnsureConnected(wifi, wait) != null && wifi.Attempts.Count == 0, "do not invent credentials for secure Wi-Fi");
        secure.Profile = "Existing campus profile";
        Check(CampusWifi.EnsureConnected(wifi, wait) == null, "reuse saved secure profile");
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml(NativeWifi.OpenProfile("WHUT-DORM"));
        Check(xml.DocumentElement.LocalName == "WLANProfile" && xml.InnerText.Contains("manual"), "temporary open network profile is valid XML");
        MustFail(delegate { NativeWifi.OpenProfile("Other"); }, "never generate profiles for unrelated networks");
    }
}
