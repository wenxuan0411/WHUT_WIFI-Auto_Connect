# WHUT Wi-Fi Auto Connect · V2.1

武汉理工大学校园网自动扫描、连接与认证工具。第二版无需 Edge、Selenium 或 msedgedriver，支持登录 Windows 后后台自动运行。

基于 [zuijiu888/Campus-Flow](https://github.com/zuijiu888/Campus-Flow) 适配；原作者信息见 [来源与致谢](THIRD_PARTY_NOTICES.md)。发布版本为 `V2.1`，EXE 文件版本为 `2.1.0.0`。

## V2.1 更新

1. 优化的开机连接校园网的功能
2. 优化的密码和界面UI显示异常的问题

## 运行

从 [V2.1 发布页](https://github.com/wenxuan0411/WHUT_WIFI-Auto_Connect/releases/tag/V2.1) 下载 `CampusFlow-WHUT-V2.1.zip`，解压后运行 `CampusFlow-WHUT.exe`。也可下载单文件 EXE；`SHA256SUMS.txt` 用于校验附件。

源码仓库不存放编译产物；自行构建后，程序位于 `outputs/CampusFlow-WHUT.exe`。

1. 保持 Windows 的 Wi-Fi 开关开启，并处于校园网覆盖范围内；无需预先连接或创建开放网络配置。
2. 双击适配版 EXE，填写校园网账号和密码，并粘贴该网络手动登录成功时的登录页网址。程序保留其中的 nasId，自动更新 IP；没有确认过默认入口可用时，不要直接使用默认值。
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

- 门户入口使用 `http://172.30.21.100/api/r/<nasId>`，设备编号来自实际成功登录的网址。不同接入网络可能使用不同编号，不枚举或猜测设备。
- 识别 HTTP 重定向和网关首页的常量 JavaScript 跳转。
- 优先读取校园网 HTTP 拦截跳转，获得本次网络的 `nasId` 等参数；未获得可信跳转时再尝试动态入口。
- 严格按学校网页提交 `nasId`、`switchip`、`wlanuserip` 和 `mac`；`userIpv4` 只取 `wlanuserip`，缺少时保持空字符串。
- 若粘贴了带 IP/nasId 的 `/tpl/whut/login.html` 或手机登录页，保留 nasId 转为对应动态入口，只刷新终端参数；不再一律回退到 default。
- 从登录页引用的 `config.js` 读取 `host_url`，忽略已注释的旧地址；当前实网为 `/api`。
- 同一个 Cookie 会话先获取 `/api/csrf-token`，再携带 `X-CSRF-Token` 提交 `/api/account/login`。
- 对 `code=0/1/2` 分别处理成功、拒绝和验证码；接口成功后仍需外网探测通过。
- 联网检查要求 HTTP 200 且正文精确匹配预期内容，不跟随探测重定向；不关闭 HTTPS 证书验证。
- 门户请求绕过系统 HTTP 代理，接口与页面必须同源，避免把账号密码提交到重定向的其他域名。
- 默认离线每 60 秒重试；联网后每 5 分钟检查一次；网络变化约 3 秒后检查。离线间隔若设为超过 5 分钟，联网检查也使用该更长间隔。
- 保留单实例后台、Windows DPAPI 加密凭据和原项目界面。

界面中认证接口、字段、附加参数和成功判定是只读信息，实际值由武理适配流程处理。填写账号密码和实际登录页网址即可；普通 HTML 表单的自动识别功能已替换为“检查门户”。

设置窗口右上角区分“后台运行中”和“已设自启，未运行”。日志包含启动版本、PID、程序路径和每次检测的耗时，可据此区分未启动、Wi-Fi 关联失败和认证拒绝。

## 设置和日志

存放在 `%LOCALAPPDATA%\CampusFlow-WHUT\`：

- `settings.xml`：门户及探测配置。
- `credentials.dat`：Windows 当前用户加密的账号密码，不可直接移到其他电脑或用户下使用。
- `campusflow.log`：运行结果，不记录账号密码、Cookie、令牌或响应正文。

使用独立的 `CampusFlow-WHUT` 自启动项和后台互斥锁。账号密码保存在当前 Windows 用户目录中，不包含在源码或发布包内。

账号密码停止输入 0.8 秒后自动加密保存，关闭窗口时补存。重新打开时回填密码并默认隐藏，点击输入框内的眼睛图标可显示或隐藏。更换账号时需重新填写对应密码；不完整输入不会覆盖已存凭据。

发布包仅包含程序、图标和公开说明，不包含任何人的账号密码、个人配置、运行日志或截图。首次使用需填写自己的信息；在已使用过本软件的 Windows 账户下运行时，会读取该账户本地保存的配置。

从旧 Selenium 版本升级时，停止旧程序及其自启动任务，运行新版重新填写账号密码。旧版 `config.txt` 和 Edge 驱动不再需要。此前使用 CampusFlow-WHUT 适配版的用户可继续使用已保存的加密配置；移动 EXE 后需重新启用自启动。

## 验证与限制

V2.1 发布前已完成：

- 构建 Windows EXE。
- 本地自动检查共 86 项，另有服务端请求校验；覆盖 Wi-Fi 目标选择、已有连接复用、失败回退、连接超时、后台重启、密码保存和主题切换。
- 本地模拟门户测试：跳转、Cookie、CSRF、参数映射、表单编码、成功与失败响应、验证码、异常 JSON、缺失参数和联网误判。
- 深浅主题下各页面的界面渲染检查。

自动检查使用隔离目录和模拟凭据，不会读取个人账号或向学校提交认证。**从其他网络实际切换、重启全流程、离线后完整登录、休眠唤醒和长期后台重连尚未完成全面实网验证**。验证码出现时需手动访问学校门户完成验证；账号被拒绝时请停止后台，修正账号密码或检查校园网状态后再启用。

若“检查门户”失败，请确认校园 Wi-Fi 已连接，并使用当前网络实际登录网址。默认入口不保证适用于所有接入网络。若接口返回成功但外网不可达，检查“运行日志”和联网检测站点是否可访问。

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

更新说明见 [V2.1 发布说明](RELEASE_NOTES.md)。
