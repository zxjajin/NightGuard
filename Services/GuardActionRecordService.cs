using System.Collections.ObjectModel;
using System.Windows;
using NightGuard.Models;

namespace NightGuard.Services;

public sealed class GuardActionRecordService
{
    private const int MaxRecords = 100;

    public ObservableCollection<GuardActionRecord> Records { get; } = [];

    public void Add(string type, string target, string action, string result, string note = "")
    {
        var record = new GuardActionRecord
        {
            Time = DateTimeOffset.Now,
            Type = type,
            Target = target,
            Action = action,
            Result = result,
            Note = note
        };

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => AddCore(record));
            return;
        }

        AddCore(record);
    }

    private void AddCore(GuardActionRecord record)
    {
        Records.Insert(0, record);
        while (Records.Count > MaxRecords)
        {
            Records.RemoveAt(Records.Count - 1);
        }
    }
}
