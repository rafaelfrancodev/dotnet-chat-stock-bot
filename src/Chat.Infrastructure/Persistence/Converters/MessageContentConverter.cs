using Chat.Domain.Messages;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Chat.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores a <see cref="MessageContent"/> as its text, so the value object costs one <c>nvarchar</c>
/// column and no extra table.
/// </summary>
/// <remarks>
/// Reading goes back through <see cref="MessageContent.Create"/> rather than around it: a row that no
/// longer satisfies the invariant is corrupt data, which is exceptional, so the failed
/// <c>Result</c> surfacing as an exception here is the intended behaviour.
/// </remarks>
internal sealed class MessageContentConverter : ValueConverter<MessageContent, string>
{
    public MessageContentConverter()
        : base(content => content.Value, value => MessageContent.Create(value).Value)
    {
    }
}
