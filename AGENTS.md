# 项目协作说明

- 项目为 Windows SSH 隧道桌面管理工具，范围见 `docs/requirements.md`。
- 修改前阅读 README 及相关文档，需求与实现变化同步记录。
- 技术栈为 .NET 8 / WinForms / SSH.NET；源码在 `src/`，测试在 `tests/`，选型见 `docs/decisions/0001-windows-stack.md`。
- 构建使用 `dotnet build SshServersManager.sln`；验证命令与覆盖范围见 `docs/testing.md`。
- 保证规则独立启停；单条停止或失败不得影响其他规则。
- 密码和私钥口令不得明文保存，日志与导出不得泄露凭据。
- 不提交真实连接配置、私钥、凭据和运行日志。
- 文档采用 UTF-8，实现后运行适当验证，交付说明结果与限制。
- 不自动推送、发布、创建远程仓库或添加未经确定的开源许可证。
