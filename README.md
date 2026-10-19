# SSH Servers Manager

面向 Windows 的桌面 SSH 隧道管理工具。通过方案组织多台 SSH 服务器的端口规则，支持双向转发、单条独立启停，以及规则和方案的保存、复制。

## 项目状态

已完成项目初始化与需求整理，尚未实现应用功能。开发语言、桌面框架、SSH 实现和存储方式待选型，因此暂未提供运行或构建命令。

## 第一版目标

- 管理多台 SSH 服务器，支持密码和私钥认证。
- 支持本地转发（`-L`）和远程转发（`-R`）。
- 端口规则独立启停、重连，展示状态与日志。
- 同一方案包含多台服务器，支持单条与批量操作。
- 保存、复制方案，将选中规则复制到已有方案或新方案。
- 提供配置导入导出、Windows 托盘、安全凭据存储和可选自动重连。

## 文档导航

- [需求规格](docs/requirements.md)：功能、交互约定与验收标准。
- [架构设计草案](docs/architecture.md)：模块、数据模型和选型验证项。
- [开发计划](docs/roadmap.md)：分阶段交付及完成条件。
- [开发与 Git 约定](CONTRIBUTING.md)。
- [变更记录](CHANGELOG.md)。

## 仓库结构

```text
ssh-servers-manager/
├── README.md
├── AGENTS.md
├── CONTRIBUTING.md
├── CHANGELOG.md
└── docs/
    ├── requirements.md
    ├── architecture.md
    └── roadmap.md
```

源码与测试目录在技术栈确定后创建。真实服务器配置、凭据、私钥和运行日志不进入仓库。当前仅建立本地仓库，未配置远程仓库或开源许可证。
