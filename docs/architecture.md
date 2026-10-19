# 架构设计草案

尚未确定开发语言、UI 框架、SSH 库或存储引擎。以下为设计约束，正式选型需验证并记录依据。

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

## 生命周期

基本流转：未启动 → 连接中 → 运行中 → 停止中 → 未启动。失败或掉线进入异常，重试重新进入连接中。

- 同一规则的操作需要协调，避免快速启停造成重复连接或失控进程。
- SSH 认证与监听确认成功后才能进入运行中。
- 自动重试有退避与可见状态；停止、取消后不得重新拉起。
- 批量操作收集各规则结果，不能因首条失败中止全批。
- 编辑运行配置采用快照，明确生效时机；覆盖启动取消、退出和休眠恢复。

每规则独立 SSH 连接作为优先验证方案。只有验证共享连接能独立撤销转发并隔离规则生命周期后，才考虑连接复用。

## 技术选型验证项

1. Windows 桌面框架的表格、托盘、打包与分发能力。
2. SSH 库或系统 OpenSSH 对密码交互、私钥口令、指纹校验、远程监听确认和取消的支持。
3. 若采用外部进程，验证退出清理与异常残留处理，敏感信息不进入命令行。
4. Windows 安全存储接口、凭据删除策略与应用身份稳定性。
5. 配置原子写入、备份、恢复与版本迁移。
6. IPv4/IPv6、通配监听、主机别名、休眠唤醒和服务端策略差异。

选型后补充决策记录、实际源码目录、依赖版本和可复现构建命令。
