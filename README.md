# StockCopilot

一个面向 **A 股研究场景** 的本地优先桌面助手（local-first trading research workstation）。

它把日常会反复切换的几类工作收进一个桌面应用里：**看盘、看市场、整理资讯、生成交易计划、沉淀本地数据**。项目当前重点不在“自动下单”，而在于把研究与决策前的准备流程做得更集中、更可控、更容易持续迭代。

## 项目定位

`StockCopilot` 是一个完整的多端协作工程，而不是单一脚本或 Demo：

- **Backend**：ASP.NET Core 8 Web API，负责数据同步、存储、配置与业务接口
- **Frontend**：Vue 3 + Vite 工作台界面，负责图表、资讯展示和交互体验
- **Desktop Shell**：.NET 8 WinForms + WebView2，将前后端封装为本地桌面应用
- **Local-first runtime**：支持本地 SQLite 运行，并保留切换数据库提供方的能力
- **Delivery pipeline**：支持 Windows 安装包、便携包、更新检测与发布链路

## 核心能力

- **股票终端**：支持分时、日 K、月 K、年 K、多种图表叠加与研究准备信息查看
- **市场总览**：通过情绪轮动页查看市场阶段、主线板块、比较窗口与实时总览
- **本地资讯库**：把个股、板块、市场多层级资讯沉淀到本地，便于检索与回看
- **交易计划**：支持草稿、总览、提醒与上下文联动，形成基础研究闭环
- **桌面交付**：安装后即可运行，不需要用户手动分别启动前后端与数据库

## 技术栈

### Backend

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core
- SQLite / SQL Server / MySQL（按配置切换）
- Swagger / OpenAPI

### Frontend

- Vue 3
- Vite
- ECharts
- KLineCharts
- Vitest / Playwright（前端测试与自动化能力）

### Desktop

- .NET 8 WinForms
- WebView2
- Windows 本地打包与安装分发

## 项目结构

```text
backend/   ASP.NET Core API、数据同步、业务模块、存储与配置
frontend/  Vue 3 工作台、图表与交互页面
desktop/   Windows 桌面壳，负责本地运行与嵌入前端
scripts/   打包、发布、自动化辅助脚本
docs/      截图与补充文档
```

## 截图

### 首页总览

![首页](docs/screenshots/首页.png)

### 股票终端

![股票终端](docs/screenshots/股票推荐.png)

### K 线图

![K线图](docs/screenshots/K线图.png)

### 板块上下文

![板块上下文](docs/screenshots/板块上下文可点击.png)

### 情绪轮动

![情绪轮动](docs/screenshots/情绪轮动.png)

### 全量资讯库

![全量资讯库](docs/screenshots/全量资讯库.png)

## 当前状态

项目仍在持续开发中，但已经具备以下可交付能力：

- Windows 桌面程序可安装、可启动、可本地运行
- 发布版可从 GitHub Releases 分发
- 桌面版本已接入更新检测
- 本地数据默认保存在用户目录，便于升级时保留配置与使用痕迹

如果你想看的是一个**正在持续迭代的产品型工程**，这个仓库能反映我在以下方向上的实践：

- 本地优先应用设计
- 桌面端封装与交付
- 后端、前端、桌面端协同开发
- 将研究流程产品化、工作台化

## 安装

推荐直接从 GitHub Releases 下载当前版本的安装包或便携包：

- `SimplerJiangAiAgent-Setup-*.exe`
- `SimplerJiangAiAgent-portable-*.zip`

发布页：<https://github.com/simplerjiang/StockCopilot/releases>

## 本地运行说明

如果你希望直接验证打包后的桌面程序，可以在仓库根目录运行：

```powershell
.\start-all.bat
```

这个脚本会重新打包并启动桌面版程序，用于验证最终交付形态，而不是仅验证浏览器开发页。

## 配置说明

项目本身不强绑定某一个固定的 LLM 服务提供方。首次启动后，请根据自己的环境配置：

- 接口地址
- 模型名称
- API Key

建议在本地开发或个人使用时填写你自己的兼容 OpenAI 接口配置，不要依赖仓库历史文档中的示例值。

## 适合谁

- 想把行情、资讯、板块观察和计划整理收进一个本地工具的人
- 更偏好本地数据、本地配置、本地可控运行方式的人
- 想研究“桌面应用 + 本地后端 + 前端工作台”这类产品形态的人

## 补充说明

完整开发记录、自动化说明和内部任务拆解仍保留在 `README.llm.md` 中；本 README 更侧重对外展示项目定位、架构和实际能力。