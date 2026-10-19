# SSH Servers Manager

面向 Windows 的桌面 SSH 隧道管理工具。通过方案组织多台 SSH 服务器的端口规则，支持双向转发、单条独立启停，以及规则和方案的保存、复制。

## 项目状态

已实现首个可运行开发版本，采用 .NET 8 / WinForms / SSH.NET。支持服务器和方案管理、双向转发、规则独立启停、复制、导入导出、Windows 安全凭据存储与托盘。尚需不同 Windows、实际 OpenSSH 策略和休眠唤醒环境验证，暂不标记为正式发布。

## 启动与构建

当前工作区可双击 `start.cmd`，或直接启动 `artifacts/windows-x64-v0.1.2/SshServersManager.exe`。发布程序需要 .NET 8 Desktop Runtime（x64）。更新后请先从托盘退出旧实例再启动新版。

源码开发需要 .NET 8 SDK：

```powershell
dotnet run --project src/SshManager.App
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Publish
powershell -ExecutionPolicy Bypass -File scripts/test.ps1
```

应用不需要管理员权限。构建、测试与发布命令的输出分别位于 `bin/`、`runtime/` 和 `artifacts/`，均不进入 Git。

## 第一版目标

- 管理多台 SSH 服务器，支持密码和私钥认证。
- 支持本地转发（`-L`）和远程转发（`-R`）。
- 端口规则独立启停、重连，展示状态与日志。
- 同一方案包含多台服务器，支持单条与批量操作。
- 粘贴 SSH 命令，预览并导入服务器与本地/远程转发规则。
- 保存、复制方案，将选中规则复制到已有方案或新方案。
- 提供配置导入导出、Windows 托盘、安全凭据存储和可选自动重连。

## 文档导航

- [需求规格](docs/requirements.md)：功能、交互约定与验收标准。
- [架构设计草案](docs/architecture.md)：模块、数据模型和选型验证项。
- [开发计划](docs/roadmap.md)：分阶段交付及完成条件。
- [开发与 Git 约定](CONTRIBUTING.md)。
- [变更记录](CHANGELOG.md)。
- [使用说明](docs/user-guide.md)：配置连接、复制方案、托盘与故障排查。
- [验证说明](docs/testing.md)：测试命令、覆盖范围与待验证环境。
- [技术选型](docs/decisions/0001-windows-stack.md)。

## 仓库结构

```text
ssh-servers-manager/
├── README.md
├── AGENTS.md
├── CONTRIBUTING.md
├── CHANGELOG.md
├── SshServersManager.sln
├── src/
│   ├── SshManager.Core/
│   └── SshManager.App/
├── tests/
├── scripts/
└── docs/
    ├── requirements.md
    ├── architecture.md
    └── roadmap.md
```

配置保存在 `%LOCALAPPDATA%\SshServersManager`，凭据存于当前 Windows 用户的凭据管理器。真实服务器配置、凭据、私钥和运行日志不进入仓库。未配置远程仓库或开源许可证。
