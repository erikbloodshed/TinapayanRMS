namespace TinapayanRMS.Tests.Common.Fixtures;

/// <summary>
/// A clock a test sets and moves by hand (<see cref="Advance"/>), for the times a save stamps (TinapayanRMSDbContext.Clock)
/// to be ones the test can name exactly, rather than whatever the system clock read, which two saves in a row can share.
/// Its local time is <see cref="Now"/> as given: the zone is UTC, so nothing shifts it.
/// </summary>
public sealed class TestClock(DateTime start) : TimeProvider
{
    /// <summary>The local time a save made now stamps.</summary>
    public DateTime Now { get; private set; } = start;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Now += by;
}
