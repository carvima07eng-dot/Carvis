namespace Carvis.Tests.Fakes;

/// <summary>A clock the test moves by hand, in a fixed time zone (UTC+2 like Madrid in summer).</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "test", "test");

    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => Zone;

    public void Advance(TimeSpan by) => Now += by;
}
