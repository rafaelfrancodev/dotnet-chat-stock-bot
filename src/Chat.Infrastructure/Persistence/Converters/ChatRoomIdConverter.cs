using Chat.Domain.ChatRooms;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chat.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="ChatRoomId"/> as the <see cref="Guid"/> it wraps. Applied by convention, so both
/// <c>ChatRooms.Id</c> and the <c>Messages.ChatRoomId</c> reference use the same column type.
/// </summary>
internal sealed class ChatRoomIdConverter : ValueConverter<ChatRoomId, Guid>
{
    public ChatRoomIdConverter()
        : base(id => id.Value, value => new ChatRoomId(value))
    {
    }
}
