# NightGuard

NightGuard 是一个本地自用的 Windows 夜间自律限制器 MVP。它的目标是减少晚上娱乐应用和娱乐网站的使用，同时尽量不影响工作、代理、截图、输入法、驱动和后台服务。

## 当前默认策略

- 默认只限制应用黑名单，例如 `steam.exe`、`WeGame.exe`
- 默认不启用“禁用非白名单前台应用”
- 默认不启用 hosts 网站限制，避免影响 Clash/TUN/系统代理
- 需要网站限制时，可以手动打开“启用网站 hosts 限制”
- 需要更强应用限制时，可以手动打开“限制期间禁用非白名单前台应用”

## 功能

- 每日限制时间段，例如 `23:30` 到次日 `07:00`
- 跨天限制时间判断
- 应用黑名单
- 始终允许应用白名单
- 可选：限制期间禁用非白名单前台应用
- 可选：通过 hosts 限制网站
- hosts 修改前自动备份
- 一键立即恢复 hosts
- 测试模式 3 分钟
- 全局临时解锁倒计时
- 设置页密码保护
- 限制期间禁止修改规则
- 当前用户开机自启，使用任务计划程序最高权限运行
- 最小化到系统托盘
- 本地 JSON 配置和日志

## 重要安全说明

NightGuard 是自律工具，不是强制管控软件：

- 不隐藏进程
- 不安装驱动
- 不禁用任务管理器
- 不阻止卸载或删除
- 不保证无法绕过

如果代理、hosts、截图、输入法等出现异常，请先运行：

```text
Emergency-Restore.bat
```

它会停止 NightGuard、移除 NightGuard 写入的 hosts 区块、删除/暂停自启动任务，并刷新 DNS。

## 运行

需要 .NET 8 SDK。

管理员运行，推荐：

```powershell
.\Run-NightGuard-Admin.bat
```

普通运行：

```powershell
dotnet run
```

修改 hosts 需要管理员权限。如果不是管理员运行，应用限制仍可工作，但网站 hosts 限制可能失败。

## 配置

运行目录中会生成：

```text
Data/config.json
Data/logs.json
```

主要配置项：

- `RestrictionStart`：限制开始时间
- `RestrictionEnd`：限制结束时间
- `BlockAllAppsDuringRestriction`：是否禁用非白名单前台应用，默认 `false`
- `EnableHostsBlocking`：是否启用 hosts 网站限制，默认 `false`
- `AlwaysAllowedProcesses`：始终允许应用白名单
- `BlockedProcesses`：应用黑名单
- `BlockedDomains`：网站黑名单
- `UnlockDelayMinutes`：全局临时解锁等待分钟数
- `TemporaryAllowanceMinutes`：全局临时放行分钟数
- `MaxUnlocksPerNight`：每晚最大全局解锁次数
- `StartWithWindows`：开机自启

## 网站限制

网站限制使用 hosts 文件实现，只写域名，不写完整 URL。

示例：

```text
douyin.com
www.douyin.com
bilibili.com
www.bilibili.com
```

注意：hosts 限制可能影响 Clash、TUN、系统代理和 DNS 行为，所以默认关闭。

## 构建

```powershell
dotnet build
```

如果 NightGuard 正在运行，构建可能因 DLL/EXE 被占用失败。请先从托盘退出 NightGuard。
