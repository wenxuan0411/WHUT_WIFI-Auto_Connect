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
                var liveSettings = AppData.LoadSettings();
                var portal = PortalClient.DiscoverPortal(liveSettings);
                Check(portal.ApiUrl.AbsoluteUri == "http://172.30.21.100/api/", "live WHUT API discovery");
                Check(portal.Parameters.ContainsKey("nasId"), "live NAS parameter available");
                Console.WriteLine("Configured portal: " + liveSettings.PortalUrl);
                Console.WriteLine("Discovered NAS: " + portal.Parameters["nasId"]);
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
            TestLifecycle();
            string root = args[0];
            Check(PortalClient.NormalizePortalUrl(root + "/tpl/whut/login.html?nasId=59&ip=10.9.9.9") == root + "/api/r/59", "preserve known NAS while refreshing terminal IP");
            var campus59 = PortalClient.DiscoverPortal(root + "/tpl/whut/login.html?acip=10.1.1.1&acname=fixture-ac&ip=10.9.9.9&nasId=59&userip=10.9.9.9&wlanacname=");
            Check(campus59.Parameters["nasId"] == "59" && campus59.Parameters["ip"] == "10.0.0.5", "successful browser URL refreshes IP but keeps NAS 59");
            Check(PortalClient.SubmitLogin(campus59, "test+user", "p&=+% 密码") == null, "submit NAS 59 and exact browser fields");
            var saved59 = PortalSettings.WhutTemplate();
            saved59.PortalUrl = root + "/tpl/whut/login.html?nasId=59&ip=10.9.9.9";
            AppData.SaveSettings(saved59);
            Check(AppData.LoadSettings().PortalUrl == root + "/api/r/59", "saved portal migration preserves NAS 59");
            var refreshed = PortalClient.DiscoverPortal(root + "/tpl/whut/login.html?nasId=stale&ip=10.9.9.9");
            Check(refreshed.Parameters["nasId"] == "14" && refreshed.Parameters["ip"] == "10.0.0.5", "refresh stale saved portal parameters before authentication");
            foreach (string probe in new[] { "campus", "campus-html", "campus-entry" })
            {
                var routed = PortalClient.DiscoverPortal(root + "/api/r/default", root + "/probe/" + probe);
                Check(routed.Parameters["nasId"] == "59", "preserve intercepted NAS instead of generic default: " + probe);
            }
            var rejected = PortalClient.DiscoverPortal(root + "/api/r/default", root + "/probe/untrusted");
            Check(rejected.Parameters["nasId"] == "14", "ignore interception to untrusted origin");
            var onlineProbe = PortalClient.DiscoverPortal(root + "/api/r/default", root + "/probe/ok");
            Check(onlineProbe.Parameters["nasId"] == "14", "online probe without redirect uses dynamic entry");
            var migrated = PortalSettings.WhutTemplate();
            migrated.PortalUrl = root + "/tpl/whut/login.html?nasId=old&ip=10.9.9.9";
            AppData.SaveSettings(migrated);
            Check(AppData.LoadSettings().PortalUrl == root + "/api/r/default", "migrate saved stale portal URL");
            Check(!File.ReadAllText(AppData.SettingsPath).Contains("10.9.9.9"), "persist normalized portal without stale terminal IP");
            Check(PortalClient.NormalizePortalUrl(root + "/tpl/whut/login_mobile.html?ip=old") == root + "/api/r/default", "normalize mobile portal URL");
            Check(PortalClient.NormalizePortalUrl(root + "/custom/start") == root + "/custom/start", "preserve custom discovery entry");
            AppData.SaveSettings(PortalSettings.WhutTemplate());
            var context = PortalClient.DiscoverPortal(root + "/ok/start");
            Check(context.ApiUrl.AbsoluteUri == root + "/ok/api/", "ignore commented host_url and preserve /api prefix");
            Check(context.Parameters["nasId"] == "14", "follow JavaScript and HTTP redirects");
            Check(PortalClient.SubmitLogin(context, "test+user", "p&=+% 密码") == null, "match browser: ip query must not override absent wlanuserip");

            foreach (string scenario in new[] { "wlan", "legacy", "stringcode" })
                Check(PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/" + scenario + "/start"), "test+user", "p&=+% 密码") == null, scenario);
            foreach (string scenario in new[] { "denied", "captcha", "unknown", "falsezero" })
                Check(PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/" + scenario + "/start"), "test+user", "p&=+% 密码") != null, "reject " + scenario);
            foreach (var failure in new Dictionary<string, string> { { "denied_ip", "终端地址" }, { "denied_password", "凭据" }, { "denied_balance", "余额" }, { "denied_limit", "在线终端" } })
            {
                string error = PortalClient.SubmitLogin(PortalClient.DiscoverPortal(root + "/" + failure.Key + "/start"), "test+user", "p&=+% 密码");
                Check(error.Contains(failure.Value) && !error.Contains("private-detail"), "classify and redact " + failure.Key);
            }
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
            TestCredentialsAndTheme();
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

    static T FormField<T>(MainForm form, string name)
    {
        return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    static void FormCall(MainForm form, string name, params object[] args)
    {
        typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    }

    static void TestCredentialsAndTheme()
    {
        AppData.SaveCredential("fixture-user", "fixture-password");
        using (var form = new MainForm())
        {
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            var user = FormField<TextBox>(form, "username");
            var pass = FormField<TextBox>(form, "password");
            Check(user.Text == "fixture-user" && pass.Text == "fixture-password" && pass.UseSystemPasswordChar,
                "reopen restores encrypted credentials with password masked");
            var eye = FormField<Button>(form, "passwordToggle");
            eye.PerformClick();
            Check(!pass.UseSystemPasswordChar, "eye reveals password");
            eye.PerformClick();
            Check(pass.UseSystemPasswordChar, "eye masks password again");
            pass.Text = "auto-saved-password";
            var until = DateTime.UtcNow.AddMilliseconds(1200);
            while (DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            Check(AppData.LoadCredential()[1] == "auto-saved-password", "password automatically saved without save button");
            Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(AppData.CredentialPath)).Contains("auto-saved-password"), "saved password is not plaintext");
            user.Text = "different-user";
            Check(pass.Text == "", "changing account clears previously filled password");
            FormCall(form, "SaveCredentialIfChanged");
            Check(AppData.LoadCredential()[0] == "fixture-user", "incomplete credentials do not overwrite saved account");
            pass.Text = "close-saved-password";
            FormCall(form, "ToggleTheme");
            var buttons = FormField<List<GlassButton>>(form, "navigationButtons");
            FormCall(form, "SelectPage", 2);
            Check(buttons[0].BackColor.GetBrightness() > 0.7F && buttons[0].ForeColor.GetBrightness() < 0.3F,
                "day navigation retains light background and dark text after changing tab");
            Check(buttons[2].ForeColor == System.Drawing.Color.White, "selected navigation has white text");
            foreach (Control control in AllControls(form))
                if (control is GlassButton)
                    Check(Contrast(control.ForeColor, control.BackColor) >= 4.5, "button contrast: " + control.Text);
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, form.ClientRectangle);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whut-ui-day-log.png"));
            }
            FormCall(form, "SelectPage", 0);
            using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, form.ClientRectangle);
                bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whut-ui-day-basic.png"));
            }
            FormCall(form, "ToggleTheme");
            Check(buttons[1].BackColor.GetBrightness() < 0.3F && buttons[1].ForeColor == System.Drawing.Color.White,
                "night navigation restores dark background and white text");
            // 检查无边框页面容器在两种主题下的实际渲染。
            for (int theme = 0; theme < 2; theme++)
            {
                for (int page = 0; page < 4; page++)
                {
                    FormCall(form, "SelectPage", page);
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, form.ClientRectangle);
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whut-ui-theme-" + theme + "-page-" + page + ".png"));
                    }
                }
                FormCall(form, "ToggleTheme");
            }
            form.Close();
        }
        using (var form = new MainForm())
        {
            Check(FormField<TextBox>(form, "username").Text == "different-user" && FormField<TextBox>(form, "password").Text == "close-saved-password",
                "closing immediately saves pending input for next launch");
            Check(FormField<TextBox>(form, "password").UseSystemPasswordChar, "new window always starts masked");
        }
        File.Delete(AppData.CredentialPath);
        AppData.SaveSettings(PortalSettings.WhutTemplate());
    }

    static IEnumerable<Control> AllControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control child in AllControls(control)) yield return child;
        }
    }

    static double Luminance(System.Drawing.Color color)
    {
        double[] channels = { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
        for (int i = 0; i < channels.Length; i++)
            channels[i] = channels[i] <= 0.04045 ? channels[i] / 12.92 : Math.Pow((channels[i] + 0.055) / 1.055, 2.4);
        return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
    }

    static double Contrast(System.Drawing.Color a, System.Drawing.Color b)
    {
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
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

    static void TestLifecycle()
    {
        string name = @"Local\CampusFlow-Test-" + Guid.NewGuid().ToString("N");
        using (var ready = new System.Threading.ManualResetEvent(false))
        using (var release = new System.Threading.ManualResetEvent(false))
        {
            var worker = new System.Threading.Thread(delegate()
            {
                using (var mutex = new System.Threading.Mutex(false, name))
                {
                    mutex.WaitOne();
                    ready.Set();
                    release.WaitOne();
                    mutex.ReleaseMutex();
                }
            });
            worker.Start();
            try
            {
                Check(ready.WaitOne(5000), "simulated old background starts");
                Check(!StartupManager.WaitForExit(name, 0), "detect actual running background via mutex");
                Check(!StartupManager.WaitForExit(name, 30), "do not restart before old background exits");
                release.Set();
                Check(StartupManager.WaitForExit(name, 5000), "restart waits for old background to release mutex");
                Check(StartupManager.WaitForExit(name, 0), "exit check leaves mutex available for new background");
            }
            finally { release.Set(); worker.Join(); }
        }
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
