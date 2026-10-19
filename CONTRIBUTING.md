# 开发与 Git 约定

## 分支与提交

- 默认分支 `main`，保持可理解、可验证的状态。
- 功能、修复、文档分支分别使用 `feat/<名称>`、`fix/<名称>`、`docs/<名称>`。
- 提交采用 `feat:`、`fix:`、`docs:`、`test:`、`chore:` 等前缀，每次聚焦一项变更。
- 提交前检查暂存差异，不提交真实连接配置、凭据、私钥、日志和构建产物。
- 不重置、覆盖或清理他人的未提交修改。
- 用户已授权公开上传至 `zhu-0731/ssh-servers-manager`；未来推送和发布仍按具体任务授权执行。

## 文档与配置

- UTF-8 文档，主要使用中文。
- 范围变化同步需求和计划；技术选型记录依据。
- README 只给出实际可用的运行与构建命令。
- 示例使用虚构数据，本机配置放在忽略目录或系统应用数据目录。
- 配置变更考虑 schema 迁移和失败恢复。

## 验证与发布

- 文档修改检查一致性、链接及 Git 差异。
- 实现按风险验证，重点覆盖双向转发、独立启停、取消、冲突、掉线、指纹和凭据安全。
- SSH 集成测试使用专用环境，记录服务端策略，不默认连接开发者真实服务器。
- 构建：`dotnet build SshServersManager.sln -c Release`；基础测试：`powershell -ExecutionPolicy Bypass -File scripts/test.ps1`；集成测试见 `docs/testing.md`。
- 首个公开预览发行版为 `v0.1.3`。发布前更新 CHANGELOG，记录验证和限制。
- 打包使用 `powershell -ExecutionPolicy Bypass -File scripts/package.ps1`，要求 Git 工作区干净；从当前提交快照构建自包含包，避免修改源码锁文件。默认运行时为已验证可用的 .NET 8.0.32。
- 输出位于 `artifacts/releases/v<版本>`，包含 ZIP 和 SHA-256。便携包按允许列表复制文件，不打包整个输出目录。
