# 架构设计草案

已采用 C# / .NET 8、WinForms、SSH.NET 和本地 JSON，详见 [选型记录](decisions/0001-windows-stack.md)。本文记录设计约束及当前实现映射。

## 模块职责

| 模块 | 职责 |
| --- | --- |
| 桌面界面 | 方案、规则、服务器管理，状态、日志和托盘 |
| 配置服务 | 校验、持久化、复制、导入导出及版本迁移 |
| 隧道管理器 | 启停、状态机、取消、批量结果和重连 |
| SSH 适配层 | 认证、指纹验证、转发监听与连接事件 |
| 凭据服务 | Windows 安全存储及凭据引用解析 |
| 日志服务 | 按规则定位事件、脱敏与保留量控制 |

## 数据模型

```text
Server
  id, name, host, sshPort, username
  authType, credentialRef, privateKeyPath
  connectTimeout, keepAliveSettings

Profile
  id, name, description

ForwardRule
  id, profileId, serverId, name
  direction: local | remote
  listenHost, listenPort, targetHost, targetPort
  reconnectPolicy

RuleRuntime（不持久化为运行事实）
  ruleId, state, effectiveConfig, lastError, startedAt
```

配置带 schemaVersion，写入应原子化并支持恢复。私钥路径与凭据引用属于本机信息，导出导入单独处理。主机信任记录独立维护，不随复制方案跳过校验。

当前实现以服务器 ID 派生 Windows 凭据目标名，不单独持久化 credentialRef。保活固定为 10 秒；连接超时可在服务器配置编辑。配置采用原子替换并保存 `.bak`，恢复目前由用户手动操作。

私钥路径为空时通过 `PrivateKeyDiscovery` 查找当前用户常用 `.ssh` 文件，连接时加载可读取的候选并交由 SSH.NET 尝试认证。手动指定路径时只使用该文件；查找按钮仅枚举文件名。尚未实现 SSH Agent 或 `ssh config` 解析。

SSH 命令由独立的 `SshCommandParser` 解析为服务器、规则和预览提示，不经过 shell，也不访问网络。UI 允许修改名称、用户名和认证方式后，构造完整候选配置，验证并持久化成功后才更新内存。原始命令和运行状态不保存。

## 生命周期

基本流转：未启动 → 连接中 → 运行中 → 停止中 → 未启动。失败或掉线进入异常，重试重新进入连接中。

- 同一规则的操作需要协调，避免快速启停造成重复连接或失控进程。
- SSH 认证与监听确认成功后才能进入运行中。
- 自动重试有退避与可见状态；停止、取消后不得重新拉起。
- 批量操作收集各规则结果，不能因首条失败中止全批。
- 编辑运行配置采用快照，明确生效时机；覆盖启动取消、退出和休眠恢复。

当前每规则独立 SSH 连接，已通过真实双向转发和独立停止测试。连接中与等待重连均可取消。自动重连仅在首次成功连接后生效；初次连接失败及认证、信任错误不自动重试。

## 后续验证项

1. Windows 桌面框架的表格、托盘、打包与分发能力。
2. SSH 库或系统 OpenSSH 对密码交互、私钥口令、指纹校验、远程监听确认和取消的支持。
3. 若采用外部进程，验证退出清理与异常残留处理，敏感信息不进入命令行。
4. Windows 安全存储接口、凭据删除策略与应用身份稳定性。
5. 配置原子写入、备份、恢复与版本迁移。
6. IPv4/IPv6、通配监听、主机别名、休眠唤醒和服务端策略差异。

实际代码位于 `src/SshManager.Core` 和 `src/SshManager.App`，SSH.NET 固定为 2026.0.0 并提交 NuGet 锁文件。当前未使用外部 SSH 进程。构建和验证命令见 README 与 [验证说明](testing.md)。
