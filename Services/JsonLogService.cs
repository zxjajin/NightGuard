using System.IO;
using System.Text.Json;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class JsonLogService
{
    private readonly object _sync = new();
    private readonly string _logPath;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public JsonLogService(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _logPath = Path.Combine(dataDirectory, "logs.json");
    }

    public int GetTemporaryUnlockCount(string nightKey)
    {
        lock (_sync)
        {
            return ReadStore().Days.FirstOrDefault(day => day.Date == nightKey)?.TemporaryUnlockCount ?? 0;
        }
    }

    public void RecordRestrictionStarted(string nightKey, DateTimeOffset time)
    {
        Mutate(nightKey, day =>
        {
            var stamp = time.ToString("yyyy-MM-dd HH:mm:ss zzz");
            if (!day.RestrictionStartedAt.Contains(stamp))
            {
                day.RestrictionStartedAt.Add(stamp);
            }
        });
    }

    public void RecordBlockedProcess(string nightKey, string processName, int processId)
    {
        Mutate(nightKey, day => day.BlockedProcesses.Add(new BlockedProcessLog
        {
            Time = DateTimeOffset.Now,
            ProcessName = processName,
            ProcessId = processId
        }));
    }

    public void RecordTemporaryUnlock(string nightKey, DateTimeOffset requestedAt, DateTimeOffset startedAt, DateTimeOffset endsAt)
    {
        Mutate(nightKey, day =>
        {
            day.TemporaryUnlockCount++;
            day.TemporaryUnlocks.Add(new TemporaryUnlockLog
            {
                RequestedAt = requestedAt,
                StartedAt = startedAt,
                EndsAt = endsAt
            });
        });
    }

    public void RecordSystemMessage(string nightKey, string message)
    {
        Mutate(nightKey, day => day.SystemMessages.Add($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {message}"));
    }

    private void Mutate(string nightKey, Action<DailyLog> update)
    {
        lock (_sync)
        {
            var store = ReadStore();
            var day = store.Days.FirstOrDefault(item => item.Date == nightKey);
            if (day is null)
            {
                day = new DailyLog { Date = nightKey };
                store.Days.Add(day);
            }

            update(day);
            File.WriteAllText(_logPath, JsonSerializer.Serialize(store, _jsonOptions));
        }
    }

    private AppLogStore ReadStore()
    {
        if (!File.Exists(_logPath))
        {
            return new AppLogStore();
        }

        var json = File.ReadAllText(_logPath);
        return JsonSerializer.Deserialize<AppLogStore>(json, _jsonOptions) ?? new AppLogStore();
    }
}
