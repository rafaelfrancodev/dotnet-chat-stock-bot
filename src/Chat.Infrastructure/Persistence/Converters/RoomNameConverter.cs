using Chat.Domain.ChatRooms;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chat.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="RoomName"/> as its normalised text, which is also what the unique index is built
/// on — two names that differ only in whitespace are already the same string by the time they get here.
/// </summary>
/// <remarks>Reads go back through <see cref="RoomName.Create"/> for the reason given on
/// <see cref="MessageContentConverter"/>.</remarks>
internal sealed class RoomNameConverter : ValueConverter<RoomName, string>
{
    public RoomNameConverter()
        : base(name => name.Value, value => RoomName.Create(value).Value)
    {
    }
}
