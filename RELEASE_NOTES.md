# releaseV2

武汉理工大学校园网自动连接工具第二版。移除 Selenium 和 Edge 驱动依赖，改为 Windows 原生 Wi-Fi 连接与 HTTP 门户认证。

## 使用

下载 `CampusFlow-WHUT-releaseV2.zip`，解压到固定目录，运行 `CampusFlow-WHUT.exe`。保持 Wi-Fi 开启，填写校园网账号密码后点击“测试登录”，确认后点击“保存并启用”。此后登录 Windows 会自动扫描、连接校园 Wi-Fi 并认证。

也可以直接下载单文件 `CampusFlow-WHUT.exe`。无需 Python、Edge 或 msedgedriver。

## 更新

- 自动扫描 `WHUT-DORM` 和 `WHUT-WLAN`，优先尝试 DORM，连接失败后尝试 WLAN。
- 已连接目标 Wi-Fi 时保持连接；没有目标网络时保留当前连接并稍后重试。
- 按当前武理门户获取动态参数、Cookie 和 CSRF 令牌，使用 `/api/account/login` 认证。
- 默认离线每 60 秒重试，联网后每 5 分钟检查，网络变化时触发检查。
- 当前用户登录 Windows 后静默自启动，单实例后台，Windows DPAPI 加密保存凭据。
- 清理旧 Python/Selenium 源码和驱动依赖；保留上游来源与署名。

## 验证范围

38 项本地自动检查及服务端请求校验通过；本机校园 Wi-Fi 扫描、现有连接识别，以及真实门户动态参数和 CSRF 获取已验证。

从其他网络实际切换、重启全流程、断网后完整认证及长期运行尚未实测。验证码需要手动完成；Wi-Fi 扫描若被拒绝，请检查 Windows 定位权限。

发布标签：`releaseV2`。EXE 内部版本：`1314.5.4.0`。附件中的 `SHA256SUMS.txt` 可用于校验下载文件。

基于 [zuijiu888/Campus-Flow](https://github.com/zuijiu888/Campus-Flow) 适配，详见仓库的来源说明。
