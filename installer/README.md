# Windows Setup 安装包

使用 Inno Setup 6.7.3，把已校验的自包含便携包封装成 Setup.exe。应用程序与 v0.1.3 便携版完全相同，不重新编译或替换已经发布的应用载荷。

## 安装行为

- 简体中文和英文向导，可选择安装目录。
- 默认当前用户安装：`%LOCALAPPDATA%\Programs\SSH Servers Manager`，无需管理员权限。
- 创建开始菜单中的启动、卸载入口；桌面快捷方式可选，默认不勾选。
- 提供 Windows“已安装的应用”卸载记录；固定 AppId 支持后续覆盖升级。
- 内置 .NET 运行时，无需额外下载安装。
- 卸载只删除安装器管理的文件与快捷方式，保留用户配置、主机信任记录、Windows 凭据及用户自行放置的文件。
- 安装器使用 Windows Restart Manager 检查目标文件是否被占用；交互升级前先从系统托盘彻底退出已安装实例，避免中断通道。

## 构建

安装 [官方 Inno Setup 6](https://jrsoftware.org/isdl.php)，或者指定已经准备好的 ISCC.exe 路径。不要把编译器提交到 Git。

```powershell
powershell -ExecutionPolicy Bypass -File scripts/installer.ps1
# 或指定编译器
powershell -ExecutionPolicy Bypass -File scripts/installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

脚本要求 `artifacts/releases/v<版本>` 下已有 `scripts/package.ps1` 生成的 ZIP 与 SHA-256；会先校验再解压，输出 `SSH-Servers-Manager-v<版本>-win-x64-Setup.exe` 和校验文件。

## 验证

```powershell
powershell -ExecutionPolicy Bypass -File scripts/test-installer.ps1
```

测试先确认当前用户没有相同 AppId 的正式安装，随后在工作区 `runtime/installer-test-*` 安装，使用独立开始菜单测试分组。检查注册记录、快捷方式目标、安装后 UI 回归、重复安装、卸载和未登记用户文件保留。桌面上没有同名快捷方式时，会创建并验证桌面快捷方式，再由测试卸载器移除；已有快捷方式时跳过此项，不覆盖用户快捷方式。测试以 `/NOCLOSEAPPLICATIONS` 执行，不关闭现有其他实例。日志保留在测试目录，测试结束清理自己的安装记录。

这不是干净虚拟机验收；无 .NET 的干净 Windows、其他系统语言和跨版本升级仍需进一步验证。

## 中文翻译来源

`languages/ChineseSimplified.isl` 来自 [Inno Setup 官方翻译](https://jrsoftware.org/files/istrans/)，保留原作者注释，固定源码提交为 `6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2`，文件声明适用于 Inno Setup 6.5.0+，已用 6.7.3 成功编译。

官方原始文件（CRLF 换行）SHA-256：`e0b0b350e2245f3c5e65586dfe43d574f6e7f06f2261149aba284954b3fc9a8d`。Git 属性保留工作区的 CRLF 换行。相关许可见 `languages/INNO-LICENSE.txt`；项目自身尚未指定开源许可证。
