using System.IO;
using System.Security.Principal;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class StartupCheckService
{
    private readonly StartupService _startupService;
    private readonly string _hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

    public StartupCheckService(StartupService startupService)
    {
        _startupService = startupService;
    }

    public StartupCheckResult Check(bool restrictionActive)
    {
        return new StartupCheckResult
        {
            CheckedAt = DateTimeOffset.Now,
            Administrator = CheckAdministrator(),
            HostsWritable = CheckHostsWritable(),
            StartupTask = CheckStartupTask(),
            RestrictionActive = new CheckItem
            {
                Name = "当前限制中",
                IsOk = !restrictionActive,
                StatusText = restrictionActive ? "是" : "否",
                Suggestion = restrictionActive ? "限制期间会按当前规则执行拦截。" : "当前不在限制模式。"
            }
        };
    }

    private static CheckItem CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            return new CheckItem
            {
                Name = "管理员权限",
                IsOk = isAdmin,
                StatusText = isAdmin ? "是" : "否",
                Suggestion = isAdmin ? "权限满足 hosts 和进程处理需求。" : "需要修改 hosts 或执行拦截时，请以管理员身份运行。"
            };
        }
        catch (Exception ex)
        {
            return Error("管理员权限", ex.Message);
        }
    }

    private CheckItem CheckHostsWritable()
    {
        try
        {
            using var stream = File.Open(_hostsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            return new CheckItem
            {
                Name = "hosts 可写",
                IsOk = true,
                StatusText = "是",
                Suggestion = "hosts 文件可写。"
            };
        }
        catch (Exception ex)
        {
            return new CheckItem
            {
                Name = "hosts 可写",
                IsOk = false,
                StatusText = "否",
                Suggestion = $"无法以读写方式打开 hosts：{ex.Message}"
            };
        }
    }

    private CheckItem CheckStartupTask()
    {
        try
        {
            var enabled = _startupService.IsEnabled();
            return new CheckItem
            {
                Name = "自启动任务",
                IsOk = enabled,
                StatusText = enabled ? "已启用" : "未启用",
                Suggestion = enabled ? "登录后会自动启动。" : "如需自启动，可在设置中开启。"
            };
        }
        catch (Exception ex)
        {
            return Error("自启动任务", ex.Message);
        }
    }

    private static CheckItem Error(string name, string message)
    {
        return new CheckItem
        {
            Name = name,
            IsOk = false,
            StatusText = "检测失败",
            Suggestion = message
        };
    }
}
