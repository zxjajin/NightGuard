# NightGuard

NightGuard 是一个本地自用的 Windows 自律限制器 MVP，用来在夜间限制娱乐应用和网站，减少熬夜。

它不是商业级管控软件，也不会隐藏进程、安装驱动或破坏系统。核心目标是：简单、透明、可维护。

## 功能概览

- 每日限制时间段，例如 `23:30` 到次日 `07:00`
- 限制时间跨天判断，例如 `23:00 - 次日 08:00`
- 限制期间可启用“禁用所有非白名单应用”
- 内置系统关键进程保护名单，避免误杀 Windows 核心进程
- 支持始终允许应用白名单
- 支持额外应用黑名单
- 被限制应用会弹出授权窗口：
  - 允许 1 分钟
  - 允许 15 分钟
  - 今晚不限此应用
  - 继续禁用
- “今晚不限此应用”只持续到本轮限制结束，第二天自动清空
- 限制网站通过 Windows `hosts` 文件实现
- 修改 `hosts` 前自动备份
- 程序退出时尽量恢复 `hosts`
- 全局临时解锁支持延迟倒计时
- 限制期间禁止修改规则
- 设置页密码保护
- 支持当前用户开机自启，通过 Windows 任务计划程序以最高权限运行
- 主程序最小化到系统托盘
- 配置和日志保存在本地 JSON 文件

## 技术栈

- C#
- .NET 8
- WPF
- Windows Forms NotifyIcon 托盘图标

## 项目结构

```text
NightGuard/
  App.xaml
  App.xaml.cs
  MainWindow.xaml
  MainWindow.xaml.cs
  AppAccessWindow.xaml
  AppAccessWindow.xaml.cs
  Models/
    AppAccessChoice.cs
    AppConfig.cs
    LogModels.cs
    RuntimeStatus.cs
  Services/
    ConfigService.cs
    HostsService.cs
    JsonLogService.cs
    PasswordService.cs
    ProcessBlockerService.cs
    RestrictionEngine.cs
    StartupService.cs
  ViewModels/
    MainViewModel.cs
  Run-NightGuard-Admin.bat
  Run-NightGuard-Admin.ps1
```

## 运行方式

需要安装 .NET 8 SDK。

普通运行：

```powershell
dotnet run
```

管理员运行，推荐：

```powershell
.\Run-NightGuard-Admin.bat
```

修改 Windows `hosts` 文件需要管理员权限。如果不是管理员运行，应用限制仍可工作，但网站限制可能写入失败。

开机自启使用 Windows 任务计划程序的 `ONLOGON` 任务，并请求 `HIGHEST` 运行级别。首次创建或更新自启任务时，请用管理员方式启动 NightGuard。

## 配置文件

配置文件位于运行目录：

```text
Data/config.json
```

主要配置项：

- `RestrictionStart`：限制开始时间，例如 `23:30`
- `RestrictionEnd`：限制结束时间，例如 `07:00`
- `BlockAllAppsDuringRestriction`：是否禁用所有非白名单应用
- `AlwaysAllowedProcesses`：始终允许应用白名单
- `BlockedProcesses`：额外应用黑名单
- `BlockedDomains`：网站黑名单，只写域名，不写 `https://`
- `UnlockDelayMinutes`：全局临时解锁等待分钟数
- `TemporaryAllowanceMinutes`：全局临时放行分钟数
- `MaxUnlocksPerNight`：每晚最大全局解锁次数
- `StartWithWindows`：是否开机自启

## 网站限制说明

网站限制通过写入 hosts 实现，例如：

```text
127.0.0.1 youtube.com
127.0.0.1 www.youtube.com
```

注意事项：

- 域名不要写成完整 URL，例如不要写 `https://chatgpt.com/`
- 推荐同时写裸域名和 `www` 域名
- 浏览器或系统 DNS 缓存可能导致短时间内仍可访问
- hosts 修改前会备份到 `Data/hosts-backups`
- 程序退出时会尽量移除 NightGuard 写入的 hosts 区块

## 安全边界

NightGuard 是自律工具，不是强制管控或安全软件：

- 不隐藏进程
- 不安装驱动
- 不禁用任务管理器
- 不阻止用户卸载或删除程序
- 不保证无法绕过

它适合个人自用，目标是制造一点“有意识的摩擦”，帮助减少夜间娱乐冲动。

## 构建

```powershell
dotnet build
```

当前代码已验证：

```text
0 个警告
0 个错误
```

## 注意

如果程序正在运行，`dotnet build` 可能因为 `NightGuard.exe` 或 `NightGuard.dll` 被占用而失败。请先从系统托盘右键退出 NightGuard，再重新构建。
