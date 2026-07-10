# WorkRecordAssistant — Windows 悬浮工作记录助手

轻量级 Windows 桌面悬浮工作记录工具，基于 **C# + WPF + .NET 8 + MVVM** 构建。

## 功能概览

- **始终置顶**悬浮窗，不遮挡日常开发工作流
- **左右边缘吸附** + **自动隐藏/滑出**（200~300ms 平滑动画）
- **编辑状态保护**：输入框聚焦时不自动隐藏
- **工作记录**：按天管理，支持时间戳显示、排序切换
- **日期导航**：上一天 / 下一天 / 今天 / 日历选择
- **一键复制**：支持自定义复制模板
- **快捷按钮**：可配置名称与链接，点击浏览器打开
- **本地存储**：SQLite + JSON 设置，无需联网
- **设置页**：主题、动画、吸附、自启动、导入/导出/备份

## 环境要求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 快速开始

```powershell
cd C:\Users\32230\WorkRecordAssistant

# 还原依赖并运行
dotnet restore
dotnet run --project src\WorkRecordAssistant\WorkRecordAssistant.csproj
```

## 发布单文件绿色版

```powershell
dotnet publish src\WorkRecordAssistant\WorkRecordAssistant.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true
```

输出目录：

```
src\WorkRecordAssistant\bin\Release\net8.0-windows\win-x64\publish\WorkRecordAssistant.exe
```

可将 `publish` 文件夹整体复制到任意位置运行（绿色版）。

## 数据存储位置

```
%LocalAppData%\WorkRecordAssistant\
├── settings.json      # 应用设置
└── workrecords.db     # SQLite 工作记录与快捷按钮
```

## 项目结构

```
WorkRecordAssistant/
├── src/WorkRecordAssistant/
│   ├── Behaviors/          # 吸附、自动隐藏
│   ├── Models/             # 数据模型
│   ├── Services/           # SQLite、设置、主题
│   ├── ViewModels/         # MVVM ViewModel
│   ├── Views/              # 设置窗口
│   ├── Resources/          # Fluent 主题与样式
│   └── Helpers/            # 复制模板等工具
└── WorkRecordAssistant.sln
```

## 复制模板变量

| 变量 | 说明 |
|------|------|
| `{date}` | 当前日期 yyyy-MM-dd |
| `{items}` | 所有记录条目 |
| `{count}` | 记录条数 |
| `{index}` | 条目序号 |
| `{content}` | 记录内容 |
| `{time}` | 创建时间 HH:mm |

默认模板示例：

```
{date}

{items}
```

条目模板：

```
{index}.
{content}
```

## 架构扩展预留

代码采用分层 + 接口抽象（`IDataService`、`ISettingsService`），后续可扩展：

Markdown 编辑、Todo、番茄钟、标签、全文搜索、云同步、插件系统、全局快捷键、AI 总结等。

## 使用提示

1. 拖动窗口靠近屏幕左/右边缘（默认 20px 内）即可吸附
2. 吸附后鼠标移到边缘细条上，窗口自动滑出
3. 点击 ⚙ 打开设置，可管理快捷按钮、主题、复制模板等
4. 输入框按 **Enter** 快速新增记录

## License

MIT
