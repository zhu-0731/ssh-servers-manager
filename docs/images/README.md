# 项目介绍配图

## 封面

文件：`hero.png`。使用内置 `image_gen` 工具，通过 imagegen 技能生成；未使用 CLI/API 回退。它是概念宣传插画，不是软件界面截图。

最终生成提示词如下，保留英文原文方便后续复用：

```text
Use case: ads-marketing. Create a polished wide landscape GitHub README cover illustration for a real Windows desktop app named SSH Servers Manager. Target roughly 1536x1024 or a wide 2:1 landscape composition if possible. Brand colors are dark navy #14243d and bright teal #38d6c5 with off-white type. Use a refined editorial technical illustration, subtle paper grain, gently dimensional terminal windows and network cables, not photorealistic, not cyberpunk. Show the workflow clearly: on the left several small overlapping terminal windows with simple abstract command lines; on the right neatly organized connection nodes linking a laptop to several servers, one small teal power symbol, tidy paths. Keep the terminals symbolic: no fabricated app UI, no detailed dashboard, no logos from other products, no claims of supported features. Bold highly readable exact main title 'SSH Servers Manager'. Exact Chinese subtitle beneath: '常用通道，点一下就连。'. Smaller English line: 'Save profiles. Start tunnels. Keep your terminal free.' Composition should use generous negative space, consistent geometry, calm premium open-source-project presentation, crisp typography. No extra text, no watermarks, no photographic people. This is an illustrative marketing cover, not a screenshot. Render all requested text accurately.
```

提示词中的风格描述不表示项目已经采用开源许可证；许可证状态以项目 README 为准。

## 真实截图

| 文件 | 内容 |
| --- | --- |
| `dashboard.png` | 真实主窗口，展示方案、服务器和双向端口规则，两条规则被勾选 |
| `command-import.png` | 真实命令解析窗口，填入示例命令并运行内置解析器展示预览 |
| `close-to-tray.png` | 真实关闭方式选择窗口 |

截图来自当前 WinForms 应用窗体，使用隔离配置与示例数据，通过原生 `DrawToBitmap` 采集。没有运行 SSH 连接，没有读写用户的连接配置、凭据或私钥，也没有合成“运行中”状态。

示例服务器使用文档地址 `192.0.2.10`、`192.0.2.20`，用户名 `dev`，方案为“日常开发”“模型实验”“临时调试”。规则包括 JupyterLab、MySQL、Web 调试和远程转发本机服务。

图片作为文档素材保存，未改变程序外观或重新打包现有发行文件。
