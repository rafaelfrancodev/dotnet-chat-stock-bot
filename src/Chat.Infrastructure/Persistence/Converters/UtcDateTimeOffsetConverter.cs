using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chat.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores an always-UTC <see cref="DateTimeOffset"/> as a <c>datetime2(7)</c> instant and restores the
/// offset and <see cref="DateTimeKind"/> on read.
/// </summary>
/// <remarks>
/// The domain normalises every timestamp with <c>ToUniversalTime()</c>, so an offset column would store
/// a constant zero in two extra bytes per row and still allow a hand-written <c>INSERT</c> to smuggle a
/// local time into the ordering key of the "last 50" query. Dropping the offset makes that
/// unrepresentable and keeps the index a plain chronological range.
/// </remarks>
internal sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTime>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.UtcDateTime,
            stored => new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc), TimeSpan.Zero))
    {
    }
}
