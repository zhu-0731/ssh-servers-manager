# 验证说明

## 环境与命令

Windows x64，.NET SDK 8.0.200，SSH.NET 2026.0.0。基础测试无需额外测试框架：

```powershell
dotnet build SshServersManager.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

真实 SSH 集成测试使用仅绑定 `127.0.0.1` 的 Paramiko 服务端和 TCP echo 服务，不使用用户服务器。安装隔离依赖后运行：

```powershell
python -m venv runtime/test-venv
runtime/test-venv/Scripts/python.exe -m pip install -r tests/fixtures/requirements.txt
powershell -ExecutionPolicy Bypass -File scripts/test.ps1 -Integration
```

测试数据和临时私钥位于 Git 忽略的 `runtime/test-*`。Windows 凭据测试使用随机 ID，完成后删除自己的测试凭据。集成服务端由测试进程启动，完成后关闭。仅在测试中自动接受隔离服务端指纹；正式应用始终要求用户确认。

## 已验证范围

已增加 SSH 命令解析的常用语法、引号、IPv6、无效参数与不支持行为检查，并将解析生成的双向规则接入真实 SSH TCP echo 测试。测试基于本机隔离服务，不代表已验证所有实际服务器环境。

v0.1.2 验证结果：Release 构建 0 警告、0 错误，32 项检查全部通过，发布版主窗口和命令导入窗口的冒烟检查退出码为 0。

- 配置校验、ID 复制隔离、引用关系、原子保存与备份。
- 导入 ID 重建、私钥路径导出清理、未知字段拒绝。
- Windows 凭据管理器写入、读取与删除。
- 主机指纹首次确认、持久化、变化拒绝和用户取消。
- 重复启动、独立停止、连接中取消、掉线重连、等待重连时停止。
- 单条失败不影响健康规则。
- 真实 SSH 密码与加密私钥认证、本地与远程 TCP 数据传输、多服务器端点并行管理。
- 命令解析后的服务器与双向转发规则可完成真实通信；解析不执行外部程序。
- 真实端口占用、远程监听拒绝、错误密码、指纹拒绝与变化、等待确认时取消、服务端断线。
- WinForms 主窗口及编辑对话框构造、显示和自动关闭。

## UI 冒烟检查

```powershell
$app = Start-Process src/SshManager.App/bin/Release/net8.0-windows/win-x64/SshServersManager.exe -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
$app.WaitForExit()
$app.ExitCode
```

使用隔离示例配置和单独的测试互斥锁，不连接服务器、不修改用户数据。成功退出码为 0；主窗口截图在程序目录的 `ui-smoke.png`。启动阶段失败时写入 `smoke-error.txt`。

## 尚需人工或额外环境验证

- 已检查当前设备 200% 缩放的窗口截图，其他 Windows 版本、缩放比例、屏幕尺寸下的交互和系统托盘仍需验证。
- 实际 OpenSSH 的 GatewayPorts、AllowTcpForwarding、主机别名和 IPv6 场景。
- 实机断网、休眠唤醒、Windows 关机和应用异常终止。
- 干净机器上的 .NET Desktop Runtime 检测和安装体验。
- 后续安装器、自包含分发和代码签名。

当前结果不能替代以上环境的验证，也不将 TCP echo 成功解释为任意业务服务健康。
