using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

// WHUT 的登录页通过 AJAX 认证；不能按普通 HTML form 提交。
static class PortalClient
{
    static readonly object Gate = new object();

    internal sealed class PortalContext
    {
        internal Uri PageUrl;
        internal Uri ApiUrl;
        internal CookieContainer Cookies = new CookieContainer();
        internal Dictionary<string, string> Parameters;
        internal string CsrfToken;
    }

    static HttpWebRequest MakeRequest(Uri url, CookieContainer cookies)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.Proxy = null; // 校园网认证直接访问网关，避免被系统代理/VPN HTTP 代理接管。
        request.Timeout = 8000;
        request.ReadWriteTimeout = 8000;
        request.AllowAutoRedirect = false;
        request.CookieContainer = cookies;
        request.UserAgent = "CampusFlow-WHUT/1.0";
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        return request;
    }

    static Uri SameOrigin(Uri source, string target)
    {
        var result = new Uri(source, WebUtility.HtmlDecode(target));
        if ((result.Scheme != "http" && result.Scheme != "https") ||
            result.GetLeftPart(UriPartial.Authority) != source.GetLeftPart(UriPartial.Authority) ||
            !String.IsNullOrEmpty(result.UserInfo))
            throw new InvalidOperationException("门户跳转或接口地址跨域，请检查登录页地址");
        return result;
    }

    static string ReadResponse(HttpWebRequest request)
    {
        using (var response = (HttpWebResponse)request.GetResponse())
        {
            if (response.StatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("接口未返回 HTTP 200");
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }
    }

    internal static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in uri.Query.TrimStart('?').Split('&'))
        {
            int separator = part.IndexOf('=');
            if (separator < 0) continue;
            values[WebUtility.UrlDecode(part.Substring(0, separator))] = WebUtility.UrlDecode(part.Substring(separator + 1));
        }
        return values;
    }

    internal static PortalContext DiscoverPortal(string portalUrl)
    {
        var context = new PortalContext { PageUrl = new Uri(portalUrl) };
        SameOrigin(context.PageUrl, portalUrl);
        string html = null;
        for (int hop = 0; hop < 6; hop++)
        {
            var request = MakeRequest(context.PageUrl, context.Cookies);
            using (var response = (HttpWebResponse)request.GetResponse())
            {
                int status = (int)response.StatusCode;
                if (status >= 300 && status < 400 && response.Headers["Location"] != null)
                {
                    context.PageUrl = SameOrigin(context.PageUrl, response.Headers["Location"]);
                    continue;
                }
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new InvalidOperationException("门户页面未返回 HTTP 200");
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) html = reader.ReadToEnd();
            }
            // 只解析门户已知的常量跳转，不执行网页 JavaScript。
            Match redirect = Regex.Match(html, @"(?:window\.)?location(?:\.href)?\s*=\s*['""]([^'""\r\n]+)['""]\s*;");
            if (redirect.Success)
            {
                context.PageUrl = SameOrigin(context.PageUrl, redirect.Groups[1].Value);
                continue;
            }
            context.Parameters = ParseQuery(context.PageUrl);
            Match config = Regex.Match(html, @"<script\b[^>]*\bsrc\s*=\s*['""]([^'""]*\bconfig\.js(?:\?[^'""]*)?)['""]", RegexOptions.IgnoreCase);
            if (!config.Success) throw new InvalidOperationException("未找到武理门户 config.js，请确认已连接 WHUT-DORM / WHUT-WLAN");
            string script = ReadResponse(MakeRequest(SameOrigin(context.PageUrl, config.Groups[1].Value), context.Cookies));
            script = Regex.Replace(script, @"/\*[\s\S]*?\*/|(?m)^\s*//[^\r\n]*", "");
            Match host = Regex.Match(script, @"\bhost_url\s*=\s*['""]([^'""]+)['""]");
            if (!host.Success) throw new InvalidOperationException("config.js 未定义 host_url");
            context.ApiUrl = SameOrigin(context.PageUrl, host.Groups[1].Value.TrimEnd('/') + "/");
            bool csrfRequired = Regex.IsMatch(html, @"\bsrc\s*=\s*['""][^'""]*\bcsrf\.js(?:\?[^'""]*)?['""]", RegexOptions.IgnoreCase);
            try
            {
                var tokenRequest = MakeRequest(new Uri(context.ApiUrl, "csrf-token"), context.Cookies);
                tokenRequest.Referer = context.PageUrl.AbsoluteUri;
                var token = JsonObject(ReadResponse(tokenRequest));
                object value;
                if (!token.TryGetValue("csrf_token", out value) || !(value is string) || String.IsNullOrWhiteSpace((string)value))
                    throw new InvalidOperationException("CSRF 令牌为空，未提交账号密码");
                context.CsrfToken = (string)value;
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                bool legacy = !csrfRequired && response != null && response.StatusCode == HttpStatusCode.NotFound;
                if (response != null) response.Close();
                if (!legacy) throw;
            }
            return context;
        }
        throw new InvalidOperationException("门户跳转次数过多");
    }

    static Dictionary<string, object> JsonObject(string text)
    {
        var result = new JavaScriptSerializer().DeserializeObject(text) as Dictionary<string, object>;
        if (result == null) throw new InvalidOperationException("接口未返回 JSON 对象");
        return result;
    }

    static string Parameter(PortalContext context, params string[] keys)
    {
        foreach (string key in keys)
        {
            string value;
            if (context.Parameters.TryGetValue(key, out value) && !String.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    internal static string SubmitLogin(PortalContext context, string username, string password)
    {
        string nasId = Parameter(context, "nasId");
        string ip = Parameter(context, "wlanuserip", "userip", "ip");
        if (nasId.Length == 0 || ip.Length == 0)
            throw new InvalidOperationException("门户未提供 nasId 或终端 IP，未提交账号密码；请使用默认登录页重新获取");
        var fields = new Dictionary<string, string>
        {
            { "username", username }, { "password", password },
            { "switchip", Parameter(context, "switchip") }, { "nasId", nasId },
            { "userIpv4", ip }, { "userMac", Parameter(context, "mac") },
            { "captcha", "" }, { "captchaId", "" }
        };
        var parts = new List<string>();
        foreach (var field in fields) parts.Add(Uri.EscapeDataString(field.Key) + "=" + Uri.EscapeDataString(field.Value));
        byte[] data = Encoding.UTF8.GetBytes(String.Join("&", parts.ToArray()));
        var request = MakeRequest(new Uri(context.ApiUrl, "account/login"), context.Cookies);
        request.Method = "POST";
        request.ContentType = "application/x-www-form-urlencoded; charset=UTF-8";
        request.Referer = context.PageUrl.AbsoluteUri;
        request.Headers["Origin"] = context.PageUrl.GetLeftPart(UriPartial.Authority);
        request.Headers["X-Requested-With"] = "XMLHttpRequest";
        if (!String.IsNullOrEmpty(context.CsrfToken)) request.Headers["X-CSRF-Token"] = context.CsrfToken;
        request.ContentLength = data.Length;
        using (var stream = request.GetRequestStream()) stream.Write(data, 0, data.Length);
        var result = JsonObject(ReadResponse(request));
        object code;
        result.TryGetValue("code", out code);
        if (Convert.ToString(code) == "2") return "需要验证码，请在校园网登录页手动完成认证";
        if (Convert.ToString(code) == "1") return "认证被拒绝，请检查账号、密码、余额或终端限制";
        if (Convert.ToString(code) != "0") return "认证接口返回未知结果，未确认成功";
        foreach (string key in new[] { "msg", "authMsg" })
        {
            object message;
            if (result.TryGetValue(key, out message) && Regex.IsMatch(Convert.ToString(message), "失败|错误|fail|error", RegexOptions.IgnoreCase))
                return "认证接口返回失败信息，未确认成功";
        }
        return null; // 仅表示接口接受认证，还必须验证外网。
    }

    internal static string TryLogin(PortalSettings settings)
    {
        if (!Monitor.TryEnter(Gate)) return "已有登录检测正在运行";
        try
        {
            string wifiError = CampusWifi.EnsureConnected();
            if (wifiError != null) return wifiError;
            if (!NetworkInterface.GetIsNetworkAvailable()) return "未检测到可用网络";
            if (HasInternet(settings)) return "网络已经可以正常访问";
            string[] credential = AppData.LoadCredential();
            if (credential == null) return "尚未保存账号密码";
            PortalContext context = DiscoverPortal(settings.PortalUrl);
            string error = SubmitLogin(context, credential[0], credential[1]);
            if (error != null) { AppData.Log(error); return error; }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Thread.Sleep(1500);
                if (HasInternet(settings)) { AppData.Log("认证成功，外网已确认连通"); return "认证成功"; }
            }
            AppData.Log("认证接口返回成功，但外网仍未连通");
            return "认证接口返回成功，但外网仍未连通";
        }
        catch (WebException ex)
        {
            var response = ex.Response as HttpWebResponse;
            string status = response == null ? ex.Status.ToString() : ((int)response.StatusCode).ToString();
            if (response != null) response.Close();
            AppData.Log("认证请求失败：" + status);
            return "认证请求失败：" + status;
        }
        catch (Exception ex)
        {
            // 不记录响应正文、Cookie、令牌和登录凭据。
            AppData.Log("认证未完成：" + ex.GetType().Name);
            return ex is InvalidOperationException ? ex.Message : "无法解析门户响应，请检查登录页地址和网络连接";
        }
        finally { Monitor.Exit(Gate); }
    }

    internal static bool HasInternet(PortalSettings settings)
    {
        if (String.IsNullOrWhiteSpace(settings.ConnectivityExpected)) return false;
        try
        {
            string content = ReadResponse(MakeRequest(new Uri(settings.ConnectivityUrl), new CookieContainer()));
            return String.Equals(content.Trim(), settings.ConnectivityExpected.Trim(), StringComparison.Ordinal);
        }
        catch { return false; }
    }
}
