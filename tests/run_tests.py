"""Compile the real C# client and exercise it against a local mock WHUT portal.

Uses only Python's standard library and Windows' .NET Framework compiler.
Never reads or submits the user's campus credentials.
"""
import http.server
import json
import os
from pathlib import Path
import subprocess
import threading
import urllib.parse

ROOT = Path(__file__).resolve().parents[1]
failures = []
posts = []


class Portal(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, body="", status=200, **headers):
        self.send_response(status)
        for key, value in headers.items():
            self.send_header(key, value)
        self.end_headers()
        self.wfile.write(body.encode("utf-8"))

    def do_GET(self):
        path = urllib.parse.urlsplit(self.path).path
        if path == "/api/r/default":
            return self.reply(status=302, Location="/ok/login.html?nasId=14&ip=10.0.0.5&switchip=10.0.0.1&mac=aa%3Abb", **{"Set-Cookie": "portal_session=fixture; Path=/"})
        if path == "/api/r/59":
            return self.reply(status=302, Location="/nas59/login.html?nasId=59&ip=10.0.0.5&userip=10.0.0.5", **{"Set-Cookie": "portal_session=fixture; Path=/"})
        if path == "/tpl/whut/login.html":
            return self.reply('<script src="/ok/config.js"></script><script src="/ok/csrf.js"></script>')
        scenario, _, tail = path.lstrip("/").partition("/")
        if scenario == "probe":
            if tail in ("campus", "campus-html", "campus-entry", "untrusted"):
                location = f"http://127.0.0.1:{self.server.server_port}/tpl/whut/login.html?nasId=59&ip=10.0.0.5"
                if tail == "campus-entry":
                    location = f"http://127.0.0.1:{self.server.server_port}/api/r/59"
                if tail == "untrusted":
                    location = "http://example.invalid/tpl/whut/login.html?nasId=59"
                if tail == "campus-html":
                    return self.reply('<script>window.location.href = "' + location.replace('&','&amp;') + '";</script>')
                return self.reply(status=302, Location=location)
            if tail == "redirect":
                return self.reply(status=302, Location="/probe/ok")
            return self.reply({"ok": "Microsoft Connect Test\n", "portal": "<html>Microsoft Connect Test</html>", "empty": ""}.get(tail, "error"), 500 if tail == "error" else 200)
        if tail == "start":
            if scenario == "crossorigin":
                return self.reply(status=302, Location="http://example.invalid/login")
            if scenario == "loop":
                return self.reply(status=302, Location=self.path)
            return self.reply("<script>window.location.href = '/" + scenario + "/default';</script>")
        if tail == "default":
            query = "nasId=14&ip=10.0.0.5&switchip=10.0.0.1&mac=aa%3Abb"
            if scenario == "wlan":
                query += "&wlanuserip=10.0.0.6"
            if scenario == "missingparams":
                query = ""
            return self.reply(status=302, Location=f"/{scenario}/login.html?{query}", **{"Set-Cookie": "portal_session=fixture; Path=/"})
        if tail == "login.html":
            if scenario == "noconfig":
                return self.reply("<html>not a WHUT portal</html>")
            html = '<script src="config.js"></script>'
            if scenario != "legacy":
                html += '<script src="csrf.js"></script>'
            return self.reply(html)
        if tail == "config.js":
            host = "http://example.invalid/api" if scenario == "crossapi" else f"/{scenario}/api"
            return self.reply("// var host_url = 'http://obsolete.invalid/api'\nvar host_url = '" + host + "'")
        if tail == "api/csrf-token":
            if scenario in ("legacy", "csrfmissing"):
                return self.reply(status=404)
            if scenario == "csrfempty":
                return self.reply('{}')
            return self.reply(json.dumps({"csrf_token": "fixture-csrf"}), **{"Set-Cookie": "csrf_session=fixture; Path=/"})
        self.reply(status=404)

    def do_POST(self):
        scenario = self.path.split("/")[1]
        posts.append(scenario)
        data = self.rfile.read(int(self.headers["Content-Length"])).decode("utf-8")
        fields = urllib.parse.parse_qs(data, keep_blank_values=True)
        expected = {"username": ["test+user"], "password": ["p&=+% 密码"], "nasId": ["14"],
                    "userIpv4": ["10.0.0.6" if scenario == "wlan" else ""],
                    "userMac": ["aa:bb"], "switchip": ["10.0.0.1"], "captcha": [""], "captchaId": [""]}
        if scenario == "nas59":
            expected.update(nasId=["59"], userMac=[""], switchip=[""])
        origin = f"http://127.0.0.1:{self.server.server_port}"
        valid = (self.path == f"/{scenario}/api/account/login" and fields == expected
                 and "portal_session=fixture" in self.headers.get("Cookie", "")
                 and self.headers.get("Origin") == origin
                 and self.headers.get("Referer", "").startswith(f"{origin}/{scenario}/login.html?")
                 and self.headers.get("X-Requested-With") == "XMLHttpRequest")
        if scenario != "legacy":
            valid = valid and self.headers.get("X-CSRF-Token") == "fixture-csrf" and "csrf_session=fixture" in self.headers.get("Cookie", "")
        if not valid:
            failures.append(scenario)
            return self.reply(status=400)
        if scenario == "postredirect":
            return self.reply(status=302, Location="/probe/ok")
        if scenario == "badjson":
            return self.reply("<html>success</html>")
        result = {"code": {"denied": 1, "captcha": 2, "unknown": 99, "stringcode": "0"}.get(scenario, 0)}
        if scenario.startswith("denied_"):
            result = {"code": 1, "authMsg": {"denied_ip": "IP不在线", "denied_password": "密码错误", "denied_balance": "余额不足", "denied_limit": "在线数超限"}[scenario] + " private-detail"}
        if scenario == "falsezero":
            result["authMsg"] = "认证失败"
        self.reply(json.dumps(result))


def main():
    out = ROOT / "tests" / "bin"
    out.mkdir(exist_ok=True)
    csc = Path(os.environ["WINDIR"]) / "Microsoft.NET" / "Framework64" / "v4.0.30319" / "csc.exe"
    if not csc.exists():
        csc = Path(str(csc).replace("Framework64", "Framework"))
    exe = out / "WhutTests.exe"
    refs = "System.dll,System.Core.dll,System.Drawing.dll,System.Security.dll,System.Xml.dll,System.Windows.Forms.dll,System.Web.Extensions.dll"
    subprocess.run([str(csc), "/nologo", "/codepage:65001", "/target:exe", "/main:WhutTests", f"/out:{exe}", f"/reference:{refs}",
                    str(ROOT / "work" / "CampusAutoLogin.cs"), str(ROOT / "work" / "WhutPortalClient.cs"), str(ROOT / "work" / "CampusWifi.cs"), str(ROOT / "tests" / "WhutTests.cs")], check=True)
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Portal)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        subprocess.run([str(exe), f"http://127.0.0.1:{server.server_port}"], check=True, timeout=60)
        assert not failures, f"Incorrect requests in scenarios: {failures}"
        assert len(posts) == 15, f"Unexpected number of login POSTs: {len(posts)}"
        assert "missingparams" not in posts
        print("PASS server-side request validation and POST count")
    finally:
        server.shutdown()
        server.server_close()


if __name__ == "__main__":
    main()
