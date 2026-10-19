# ADR 0001：Windows 桌面技术栈

状态：采用。

## 决策

- C# / .NET 8，WinForms 原生桌面界面。
- SSH.NET 2026.0.0，使用库 API，不调用外部 ssh.exe。
- 每规则独立 SshClient 与转发对象，不共享 SSH 连接。
- JSON 保存非敏感配置，带 schemaVersion，使用原子替换和上一版本备份。
- 密码和私钥口令使用 Windows Credential Manager 的通用凭据，按当前 Windows 用户存储。
- SHA-256 主机指纹单独记录，以主机、SSH 端口和主机密钥算法索引。

## 依据与取舍

现有 Windows 环境具备 .NET 8 SDK。WinForms 提供表格、托盘和文件对话框，适合当前单平台范围。SSH.NET 直接支持密码、加密私钥和本地/远程转发，避免额外维护外部进程、口令交互和命令行转义。

独立连接会增加服务端连接数，但让规则的取消、重连和停止保持独立。第一版优先保证行为，暂不优化连接复用。

配置与凭据分离，导入生成新 ID，避免导入文件复用现有凭据。主机指纹变化直接拒绝，当前需通过数据目录手动维护信任记录。

发布采用 win-x64 单文件、框架依赖模式，需要 .NET 8 Desktop Runtime。自包含包与安装器后续提供。

## 参考

- [SSH.NET 项目](https://github.com/sshnet/SSH.NET)
- [远程转发 API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.ForwardedPortRemote.html)
- [转发生命周期 API](https://sshnet.github.io/SSH.NET/api/Renci.SshNet.ForwardedPort.html)

## 验证

本项目测试覆盖真实 SSH 密码、加密私钥、双向 TCP 通信、独立停止与监听失败。平台和测试范围详见 [验证说明](../testing.md)。
