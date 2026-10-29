using System.Globalization;
using System.Text.Json;

namespace Twentea.Core;

/// <summary>Append-only break history in history.jsonl, plus today's counts. Same format as the Mac app.</summary>
public sealed class StatsLog : IStatsSink
{
    private sealed class BreakEvent
    {
        public string Date { get; set; } = "";
        public BreakKind Kind { get; set; }
        public BreakOutcome Outcome { get; set; }
    }

    private static readonly JsonSerializerOptions LineOptions = new(Json.Options) { WriteIndented = false };

    private readonly string _file;
    private DateTime _dayStart;

    public Dictionary<BreakOutcome, int> Today { get; } = [];
    public bool Enabled { get; set; } = true;

    /// <summary>Raised after a new event is recorded.</summary>
    public event Action? Changed;

    public StatsLog(string? directory = null)
    {
        directory ??= SettingsStore.DefaultDirectory;
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "history.jsonl");
        _dayStart = Scheduler.DayStart(DateTime.Now);
        LoadToday();
    }

    public int Count(BreakOutcome outcome) => Today.GetValueOrDefault(outcome);

    /// <summary>Share of breaks taken (done or credited) out of taken plus skipped, or null before the first one.</summary>
    public int? Adherence
    {
        get
        {
            var taken = Count(BreakOutcome.Done) + Count(BreakOutcome.Credited);
            var total = taken + Count(BreakOutcome.Skipped);
            return total == 0 ? null : (int)Math.Round(taken * 100.0 / total);
        }
    }

    public void Record(BreakKind kind, BreakOutcome outcome, DateTime at)
    {
        if (!Enabled) return;
        RollDayIfNeeded(at);
        Today[outcome] = Count(outcome) + 1;
        var line = JsonSerializer.Serialize(new BreakEvent
        {
            Date = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            Kind = kind,
            Outcome = outcome,
        }, LineOptions);
        try { File.AppendAllText(_file, line + "\n"); }
        catch (IOException) { }
        Changed?.Invoke();
    }

    private void RollDayIfNeeded(DateTime date)
    {
        var start = Scheduler.DayStart(date);
        if (start == _dayStart) return;
        _dayStart = start;
        Today.Clear();
    }

    private void LoadToday()
    {
        if (!File.Exists(_file)) return;
        foreach (var line in File.ReadLines(_file))
        {
            try
            {
                var e = JsonSerializer.Deserialize<BreakEvent>(line, LineOptions);
                if (e is null) continue;
                var date = DateTime.Parse(e.Date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToLocalTime();
                if (date >= _dayStart) Today[e.Outcome] = Count(e.Outcome) + 1;
            }
            catch (Exception ex) when (ex is JsonException or FormatException) { }
        }
    }
}
