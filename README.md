# DefaultAppLocker

DefaultAppLocker 是一个轻量级 Windows 默认应用配置管理工具，用于保存、恢复、比较和锁定当前用户的默认应用关联配置。

它的定位不是替代 Windows 设置应用，而是作为系统工具补充：

- Windows 设置负责修改默认应用。
- DefaultAppLocker 负责保存当前状态、恢复到目标状态、比较差异、快速套用常见配置，并在需要时保持配置不被意外改变。

界面风格以 Windows 11 系统工具为目标，参考 Windows Settings、PowerToys、Windows Security，并加入轻量的 Fluent / Mica / 玻璃层次感。

---

## 功能特性

- 扫描当前用户的默认应用关联。
- 保存默认应用配置快照。
- 管理多个快照配置。
- 比较当前 Windows 默认应用与目标配置的差异。
- 恢复指定快照配置。
- 快捷配置常用默认应用类型：
  - 浏览器
  - PDF
  - 图片查看器
  - 视频播放器
  - 音频播放器
  - 文本编辑器
  - Microsoft Office
- 支持默认应用模板方案保存与套用。
- 支持登录自动恢复。
- 支持持续锁定。
- 支持配置导入/导出。
- 提供命令行接口，便于自动化和回归测试。

---

## 工作方式

DefaultAppLocker 会读取当前用户的文件扩展名和协议关联，并保存为 JSON 配置。

实际应用配置时，程序会生成临时 SetUserFTA 配置文件，并调用同目录下的 `SetUserFTA.exe`。

因此发布目录建议保持：

```text
DefaultAppLocker.exe
DefaultAppLocker.Core.dll
SetUserFTA.exe
```

如果未检测到 `SetUserFTA.exe`，仍可以使用扫描、保存、比较、快照管理、模板管理等功能，但无法真正应用默认应用配置。

---

## 配置目录

默认配置目录：

```text
%AppData%\DefaultAppLocker\
```

主要内容：

```text
Config.json
Snapshots\
SnapshotProfiles\
QuickProfiles\
Logs\
```

说明：

- 不使用数据库。
- 配置文件为 JSON。
- 快照和模板方案均为独立文件，便于备份和迁移。

自动回归测试可通过环境变量指定隔离配置目录：

```powershell
$env:DEFAULTAPPLOCKER_CONFIG_ROOT = "D:\Temp\DefaultAppLocker-Test"
```

正常使用时无需设置该变量。

---

## 命令行接口

所有命令行模式均为静默模式，不启动 GUI。

```powershell
DefaultAppLocker.exe --capture-snapshot [别名]
DefaultAppLocker.exe --export-all <path.json>
DefaultAppLocker.exe --export-snapshots <path.json>
DefaultAppLocker.exe --export-templates <path.json>
DefaultAppLocker.exe --import <path.json>
DefaultAppLocker.exe --apply-snapshot <id|alias|latest>
DefaultAppLocker.exe --apply-template <id|alias|latest>
DefaultAppLocker.exe --restore
DefaultAppLocker.exe --help
```

常见示例：

```powershell
# 保存当前默认应用状态为快照
.\DefaultAppLocker.exe --capture-snapshot "Clean Windows Setup"

# 导出全部配置
.\DefaultAppLocker.exe --export-all .\DefaultAppLocker-Backup.json

# 导入配置包
.\DefaultAppLocker.exe --import .\DefaultAppLocker-Backup.json

# 应用最新快照
.\DefaultAppLocker.exe --apply-snapshot latest
```

---

## 构建

要求：

- Windows 10/11
- .NET 8 SDK

构建：

```powershell
dotnet build .\DefaultAppLocker.slnx -c Release -v:minimal
```

运行测试：

```powershell
dotnet test .\DefaultAppLocker.slnx -c Release -v:minimal
```

发布：

```powershell
dotnet publish .\DefaultAppLocker\DefaultAppLocker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=false `
  -o .\publish\win-x64
```

---

## 自动回归测试

项目包含命令行自动回归脚本：

```text
scripts\Run-Regression.ps1
```

运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-Regression.ps1
```

该脚本会自动执行：

1. Release 编译。
2. 单元测试。
3. 发布命令行可执行文件。
4. 在隔离配置目录中调用 CLI。
5. 验证导出、导入、快照创建和错误参数处理。
6. 输出最终结果：

```text
PASS
```

或：

```text
FAIL
Failure reasons:
- ...
```

---

## 项目结构

```text
DefaultAppLocker.slnx
DefaultAppLocker\              # WPF 桌面应用 / UI 层
DefaultAppLocker.Core\         # 核心逻辑、配置、快照、模板、命令行
DefaultAppLocker.Tests\        # xUnit 测试
scripts\Run-Regression.ps1     # CLI 自动回归测试
```

---

## 设计原则

- 保持轻量、快速启动、易维护。
- 不引入大型第三方 UI 框架。
- 核心逻辑与 UI 分离。
- 默认应用修改仍交给 Windows 和 SetUserFTA。
- 所有配置尽量保持透明、可备份、可迁移。

---

## 注意事项

- 修改默认应用属于当前用户级别行为。
- Windows 默认应用机制可能因系统版本而变化。
- 应用配置前建议先保存当前快照。
- 使用登录自动恢复或持续锁定前，请确认目标快照正确。

---

## 许可证

请根据仓库实际授权补充许可证信息。
