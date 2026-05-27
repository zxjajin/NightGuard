using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace NightGuard.Services;

public sealed class StartupService
{
    private const string TaskName = "NightGuard";

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            CreateScheduledTask();
        }
        else
        {
            DeleteScheduledTask();
        }
    }

    public bool IsEnabled()
    {
        return RunSchtasks(["/Query", "/TN", TaskName]).ExitCode == 0;
    }

    private static void CreateScheduledTask()
    {
        var launchCommand = BuildLaunchCommand();
        var result = RunSchtasks([
            "/Create",
            "/TN", TaskName,
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/F",
            "/TR", launchCommand
        ]);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create startup task: {result.Error}{result.Output}");
        }
    }

    private static void DeleteScheduledTask()
    {
        RunSchtasks(["/Delete", "/TN", TaskName, "/F"]);
    }

    private static string BuildLaunchCommand()
    {
        var processPath = Environment.ProcessPath ?? "";
        var assemblyPath = Assembly.GetEntryAssembly()?.Location ?? "";

        if (Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            && assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{processPath}\" \"{assemblyPath}\"";
        }

        return $"\"{processPath}\"";
    }

    private static (int ExitCode, string Output, string Error) RunSchtasks(IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
