using Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chat.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="MessageId"/> as the <see cref="Guid"/> it wraps, so the strongly-typed id costs
/// nothing at the database boundary.
/// </summary>
internal sealed class MessageIdConverter : ValueConverter<MessageId, Guid>
{
    public MessageIdConverter()
        : base(id => id.Value, value => new MessageId(value))
    {
    }
}
