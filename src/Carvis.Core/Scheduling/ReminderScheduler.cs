using Carvis.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carvis.Core.Scheduling;

/// <summary>
/// Checks the reminders every few seconds and raises <see cref="ReminderDue"/> when one is due.
/// Reminders that came due while the PC was off fire on the next start (marked as late).
/// </summary>
public sealed class ReminderScheduler(IReminderStore store, TimeProvider time, ILogger<ReminderScheduler>? logger = null) : IDisposable
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LateAfter = TimeSpan.FromMinutes(2);

    private readonly ILogger _logger = logger ?? NullLogger<ReminderScheduler>.Instance;
    private readonly object _lock = new();
    private ITimer? _timer;

    /// <summary>Raised on a thread-pool thread. The bool says it is late (missed while the PC was off).</summary>
    public event Action<Reminder, bool>? ReminderDue;

    public void Start()
    {
        _timer ??= time.CreateTimer(_ => Check(), null, TimeSpan.FromSeconds(3), CheckInterval);
    }

    public void Check()
    {
        lock (_lock)
        {
            var now = time.GetLocalNow();
            IReadOnlyList<Reminder> due;
            try
            {
                due = store.Due(now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read reminders");
                return;
            }

            foreach (var reminder in due)
            {
                var late = now - reminder.DueAt > LateAfter;
                if (reminder.Recurrence == Recurrence.None)
                    store.MarkFired(reminder.Id);
                else
                    store.Reschedule(reminder.Id, NextOccurrence(reminder.DueAt, reminder.Recurrence, now));

                _logger.LogInformation("Reminder {Id} due", reminder.Id);
                try
                {
                    ReminderDue?.Invoke(reminder, late);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reminder handler failed");
                }
            }
        }
    }

    /// <summary>The first occurrence after <paramref name="now"/> (skipping the ones missed while off).</summary>
    public static DateTimeOffset NextOccurrence(DateTimeOffset due, Recurrence recurrence, DateTimeOffset now)
    {
        var next = due;
        do
        {
            next = recurrence switch
            {
                Recurrence.Hourly => next.AddHours(1),
                Recurrence.Daily => next.AddDays(1),
                Recurrence.Weekly => next.AddDays(7),
                Recurrence.Weekdays => NextWeekday(next),
                _ => throw new ArgumentOutOfRangeException(nameof(recurrence)),
            };
        }
        while (next <= now);
        return next;
    }

    private static DateTimeOffset NextWeekday(DateTimeOffset value)
    {
        var next = value.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            next = next.AddDays(1);
        return next;
    }

    public void Dispose() => _timer?.Dispose();
}
