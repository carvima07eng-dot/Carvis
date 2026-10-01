using System.Text.Json.Nodes;
using Carvis.Core.Chat;
using Carvis.Core.Scheduling;
using Carvis.Core.Storage;
using Carvis.Core.Tools;
using Carvis.Core.Tools.Productivity;
using Carvis.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Carvis.Tests.Productivity;

public sealed class ProductivityTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 9, 0, 0, TimeSpan.FromHours(2)); // a Friday
    private readonly string _dir = Directory.CreateTempSubdirectory("carvis-prod").FullName;
    private readonly CarvisDatabase _db;
    private readonly ManualTime _time = new(Start);
    private readonly ContentProtector _protector = new();

    public ProductivityTests() => _db = new CarvisDatabase(Path.Combine(_dir, "carvis.db"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Scheduler_FiresDueRemindersOnceAndReschedulesRecurringOnes()
    {
        var store = new SqliteReminderStore(_db, _protector, _time);
        var once = store.Add("Llamar a mamá", Start.AddMinutes(5));
        var daily = store.Add("Tomar la pastilla", Start.AddMinutes(10), Recurrence.Daily);
        using var scheduler = new ReminderScheduler(store, _time);
        var fired = new List<(Reminder Reminder, bool Late)>();
        scheduler.ReminderDue += (r, late) => fired.Add((r, late));

        scheduler.Check();
        Assert.Empty(fired);

        _time.Advance(TimeSpan.FromMinutes(11));
        scheduler.Check();
        scheduler.Check();

        Assert.Equal(["Llamar a mamá", "Tomar la pastilla"], fired.Select(f => f.Reminder.Text));
        Assert.Single(store.Pending());
        Assert.Equal(daily.Id, store.Pending()[0].Id);
        Assert.Equal(Start.AddMinutes(10).AddDays(1), store.Pending()[0].DueAt);
        Assert.DoesNotContain(store.Pending(), r => r.Id == once.Id);
    }

    [Fact]
    public void Scheduler_MarksRemindersMissedWhileOffAsLate()
    {
        var store = new SqliteReminderStore(_db, _protector, _time);
        store.Add("Entregar la práctica", Start.AddHours(-3));
        using var scheduler = new ReminderScheduler(store, _time);
        var late = false;
        scheduler.ReminderDue += (_, l) => late = l;

        scheduler.Check();

        Assert.True(late);
    }

    [Fact]
    public void NextOccurrence_SkipsWeekendsAndMissedOnes()
    {
        var friday = new DateTimeOffset(2026, 10, 2, 8, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal(DayOfWeek.Monday, ReminderScheduler.NextOccurrence(friday, Recurrence.Weekdays, friday).DayOfWeek);
        Assert.Equal(friday.AddDays(7), ReminderScheduler.NextOccurrence(friday, Recurrence.Weekly, friday.AddDays(3)));
        Assert.Equal(friday.AddDays(3), ReminderScheduler.NextOccurrence(friday, Recurrence.Daily, friday.AddDays(2).AddHours(1)));
    }

    [Fact]
    public async Task ReminderTool_AcceptsMinutesOrABareTime()
    {
        var reminders = new SqliteReminderStore(_db, _protector, _time);
        var tool = new CreateReminderTool(reminders, new SqliteRoutineStore(_db, _time), _time);

        await tool.ExecuteAsync(new ToolArguments(new JsonObject { ["texto"] = "Sacar la pizza", ["en_minutos"] = 12 }), ToolContext.Default);
        await tool.ExecuteAsync(new ToolArguments(new JsonObject { ["texto"] = "Clase", ["fecha_hora"] = "08:30", ["repetir"] = "laborables" }), ToolContext.Default);

        var pending = reminders.Pending();
        Assert.Equal(Start.AddMinutes(12), pending[0].DueAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 8, 30, 0, TimeSpan.FromHours(2)), pending[1].DueAt); // 08:30 already passed today
        Assert.Equal(Recurrence.Weekdays, pending[1].Recurrence);

        Assert.Throws<ToolArgumentException>(() =>
            tool.Preview(new ToolArguments(new JsonObject { ["texto"] = "x", ["fecha_hora"] = "2020-01-01T10:00" }), ToolContext.Default));
    }

    [Fact]
    public void NotesAndTasks_RoundTrip()
    {
        var notes = new SqliteNoteStore(_db, _protector, _time);
        notes.Add("La contraseña del wifi de clase está en la pizarra");
        Assert.Single(notes.All());

        var tasks = new SqliteTodoStore(_db, _protector, _time);
        var later = tasks.Add("Estudiar Acceso a Datos", null);
        var first = tasks.Add("Entregar práctica", Start.AddDays(2));
        Assert.Equal([first.Id, later.Id], tasks.All().Select(t => t.Id)); // with a due date first
        tasks.SetDone(first.Id, true);
        Assert.Single(tasks.All());
        Assert.Equal(2, tasks.All(includeDone: true).Count);
    }

    [Fact]
    public async Task Routines_AreValidatedAndRunTheirStepsAsFollowUps()
    {
        var routines = new SqliteRoutineStore(_db, _time);
        var echo = new EchoTool();
        IToolRegistry? registry = null;
        var services = new ServiceCollection().AddSingleton(_ => registry!).BuildServiceProvider();
        var create = new CreateRoutineTool(routines, services);
        var run = new RunRoutineTool(routines);
        registry = new ToolRegistry([echo, create, run]);

        var steps = new JsonArray(
            new JsonObject { ["herramienta"] = "eco", ["argumentos"] = new JsonObject { ["texto"] = "uno" } },
            new JsonObject { ["herramienta"] = "eco", ["argumentos"] = new JsonObject { ["texto"] = "dos" } });
        var created = await create.ExecuteAsync(new ToolArguments(new JsonObject { ["nombre"] = "Modo estudio", ["pasos"] = steps }), ToolContext.Default);
        Assert.True(created.Success, created.Output);

        var nested = new JsonArray(new JsonObject { ["herramienta"] = "ejecutar_rutina", ["argumentos"] = new JsonObject { ["nombre"] = "x" } });
        Assert.Throws<ToolArgumentException>(() => create.Preview(new ToolArguments(new JsonObject { ["nombre"] = "bucle", ["pasos"] = nested }), ToolContext.Default));

        var executor = new ToolExecutor(registry);
        var outputs = new List<string>();
        await foreach (var step in executor.RunAsync([new ToolCall("ejecutar_rutina", new JsonObject { ["nombre"] = "modo ESTUDIO" })], false))
        {
            if (step.Event is ToolFinished finished)
                outputs.Add(finished.Result.Output);
        }

        Assert.Equal(3, outputs.Count);
        Assert.Equal(["uno", "dos"], outputs.Skip(1));
    }

    private sealed class EchoTool : ITool
    {
        public string Name => "eco";
        public string Description => "Repite un texto";
        public string Category => "test";
        public JsonObject Parameters { get; } = Schema.Object(Schema.Required("texto", Schema.String("texto")));
        public ToolPreview Preview(ToolArguments arguments, ToolContext context) => new("eco " + arguments.String("texto"), ToolRisk.Read);
        public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(ToolResult.Ok(arguments.String("texto")));
    }
}
