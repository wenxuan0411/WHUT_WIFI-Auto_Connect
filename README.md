# WHUT Wi-Fi Auto Connect · releaseV2

武汉理工大学校园网自动扫描、连接与认证工具。第二版无需 Edge、Selenium 或 msedgedriver，支持登录 Windows 后后台自动运行。

基于 [zuijiu888/Campus-Flow](https://github.com/zuijiu888/Campus-Flow) 适配；原作者信息见 [来源与致谢](THIRD_PARTY_NOTICES.md)。EXE 内部版本为 `1314.5.4.0`。

## 运行

从 [releaseV2 发布页](https://github.com/wenxuan0411/WHUT_WIFI-Auto_Connect/releases/tag/releaseV2) 下载 `CampusFlow-WHUT-releaseV2.zip`，解压后运行 `CampusFlow-WHUT.exe`。也可下载单文件 EXE。

源码仓库不存放编译产物；自行构建后，程序位于 `outputs/CampusFlow-WHUT.exe`。

1. 保持 Windows 的 Wi-Fi 开关开启，并处于校园网覆盖范围内；无需预先连接或创建开放网络配置。
2. 双击适配版 EXE，填写校园网账号和密码。登录页地址保留默认值。
3. 点击“测试登录”会自动扫描、连接校园 Wi-Fi，然后尝试认证；已连接目标 Wi-Fi 时直接复用连接。
4. 连接成功后，可点击“检查门户”只验证参数和令牌获取；已关联目标 Wi-Fi 且外网可用时，不重复提交认证。
5. 点击“保存并启用”，启用当前用户登录 Windows 后自动扫描、连接及认证。此后可以关闭设置窗口。
6. 需要退出后台或取消自启动时，重新打开程序，点击“停止后台”。

程序无需 Python、Edge、msedgedriver 或管理员权限。依赖 Windows 自带的 .NET Framework 4.x；已在当前 Windows 电脑构建和检查。
程序通过 Windows Native Wi-Fi API 主动扫描和关联，不删除或改写现有 Wi-Fi 配置。已有配置时复用；开放校园网没有配置时使用临时配置。若目标 Wi-Fi 需要无线密码或 802.1X，须先在 Windows 中保存可用配置。
把 EXE 放在固定目录再启用；移动 EXE 后需在新位置重新点击“保存并启用”。

## 自动连接策略

- 已连接 `WHUT-DORM` 或 `WHUT-WLAN`：保持连接，直接检查是否需要认证。
- 未连接目标网络：主动扫描，优先尝试 `WHUT-DORM`，连接失败再尝试 `WHUT-WLAN`；支持多无线网卡。
- 即使其他 Wi-Fi 已能上网，只要找到校园网也会尝试切换；没有目标网络时不主动断开现有连接。
- 每次连接最多等待约 15 秒，并确认实际关联的 SSID；请求被接受不等于已经连接。
- 默认离线每 60 秒重试。Wi-Fi 关闭、飞行模式、无线服务未启动或权限不足时会记录原因。
- 若提示访问被拒绝，请在 Windows 位置设置中开启定位服务和允许应用访问位置，随后重试。

实现依据：[Microsoft 扫描接口](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanscan)、[连接参数及临时配置](https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/ns-wlanapi-wlan_connection_parameters)。

## 学校适配

- 门户入口：`http://172.30.21.100/api/r/default`。
- 识别 HTTP 重定向和网关首页的常量 JavaScript 跳转。
- 从本次门户跳转获取 `nasId`、`switchip`、IP 和 MAC；支持 `wlanuserip`、`userip` 和当前实网返回的 `ip`。
- 从登录页引用的 `config.js` 读取 `host_url`，忽略已注释的旧地址；当前实网为 `/api`。
- 同一个 Cookie 会话先获取 `/api/csrf-token`，再携带 `X-CSRF-Token` 提交 `/api/account/login`。
- 对 `code=0/1/2` 分别处理成功、拒绝和验证码；接口成功后仍需外网探测通过。
- 联网检查要求 HTTP 200 且正文精确匹配预期内容，不跟随探测重定向；不关闭 HTTPS 证书验证。
- 门户请求绕过系统 HTTP 代理，接口与页面必须同源，避免把账号密码提交到重定向的其他域名。
- 默认离线每 60 秒重试；联网后每 5 分钟检查一次；网络变化约 3 秒后检查。离线间隔若设为超过 5 分钟，联网检查也使用该更长间隔。
- 保留单实例后台、Windows DPAPI 加密凭据和原项目界面。

界面中认证接口、字段、附加参数和成功判定是只读信息，实际值由武理适配流程处理。只需填写账号密码；普通 HTML 表单的自动识别功能已替换为“检查门户”。

## 设置和日志

存放在 `%LOCALAPPDATA%\CampusFlow-WHUT\`：

- `settings.xml`：门户及探测配置。
- `credentials.dat`：Windows 当前用户加密的账号密码，不可直接移到其他电脑或用户下使用。
- `campusflow.log`：运行结果，不记录账号密码、Cookie、令牌或响应正文。

使用独立的 `CampusFlow-WHUT` 自启动项和后台互斥锁。账号密码保存在当前 Windows 用户目录中，不包含在源码或发布包内。

从旧 Selenium 版本升级时，停止旧程序及其自启动任务，运行新版重新填写账号密码。旧版 `config.txt` 和 Edge 驱动不再需要。此前使用 CampusFlow-WHUT 适配版的用户可继续使用已保存的加密配置；移动 EXE 后需重新启用自启动。

## 验证与限制

2026-09-22 已完成：

- 构建 Windows EXE。
- 本地自动检查共 38 项，另有服务端请求校验；Wi-Fi 测试覆盖目标选择、已有连接复用、其他网络切换、失败回退、连接超时、无目标网络及安全网络配置处理。
- 本机 Native Wi-Fi 扫描与当前校园连接识别通过，扫描没有断开已有连接。
- 本地模拟门户测试：跳转、Cookie、CSRF、参数映射、表单编码、成功与失败响应、验证码、异常 JSON、缺失参数和联网误判。
- 当前 `WHUT-DORM` 实网只读验证：动态参数、真实 API 地址和 CSRF 令牌均能获取，当前外网探测通过。

当前电脑已经联网，因此没有断开现有连接，也没有向学校实际提交账号密码。**从其他网络实际切换、重启全流程、离线后完整登录、休眠唤醒和长期后台重连尚未经过实网验证**；`WHUT-WLAN` 亦未单独验证。验证码出现时需手动访问学校门户完成验证；账号被拒绝时请停止后台，修正账号密码或检查校园网状态后再启用。

若“检查门户”失败，请确认校园 Wi-Fi 已连接且默认入口可访问。不要手动填入上一台设备或上一次登录的 IP/nasId。若接口返回成功但外网不可达，检查“运行日志”和联网检测站点是否可访问。

## 开发和测试

重新构建前，请在设置窗口点击“停止后台”并关闭窗口，避免 EXE 被占用。构建成功后重新启用。编译失败会保留旧 EXE。

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\work\Build-CampusAutoLogin.ps1
python .\tests\run_tests.py
# 可选：仅在武理校园网执行只读检查，不提交凭据
.\tests\bin\WhutTests.exe --live
# 校园 Wi-Fi 已连接时：验证扫描及保持现有连接，不提交凭据
.\tests\bin\WhutTests.exe --wifi-live
```

## 源码结构

```text
work/
  CampusAutoLogin.cs           界面、后台、自启动和凭据保存
  CampusWifi.cs                Windows 原生 Wi-Fi 扫描与连接
  WhutPortalClient.cs          武理 HTTP 认证协议
  Build-CampusAutoLogin.ps1    Windows 构建脚本
  assets/CampusFlow.ico        应用图标
tests/
  WhutTests.cs                客户端及 Wi-Fi 测试
  run_tests.py                本地模拟认证门户与测试入口
```

详细变更见 [releaseV2 发布说明](RELEASE_NOTES.md)。
