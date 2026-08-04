using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Messages;
using Chat.Application.Errors;
using Chat.Domain.Common;

namespace Chat.Application.Features.Messages.GetLatestMessages;

/// <summary>
/// Serves <see cref="GetLatestMessagesQuery"/>: confirm the room exists, then hand back the projection
/// the repository produced.
/// </summary>
/// <remarks>
/// Deliberately thin. The filter, the ordering, the limit and the projection all run in SQL
/// (<c>IMessageRepository.GetLatestAsync</c>), and the result is already oldest to newest, so re-sorting
/// or re-paging here would only duplicate work the database has done — and would quietly make the
/// "never load the full history" guarantee unverifiable.
/// </remarks>
/// <param name="chatRooms">Existence check, so an unknown room is an expected failure.</param>
/// <param name="messages">The read side of the message store.</param>
internal sealed class GetLatestMessagesHandler(
    IChatRoomRepository chatRooms,
    IMessageRepository messages)
    : IQueryHandler<GetLatestMessagesQuery, IReadOnlyList<MessageDto>>
{
    public async Task<Result<IReadOnlyList<MessageDto>>> Handle(
        GetLatestMessagesQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool roomExists = await chatRooms
            .ExistsAsync(request.ChatRoomId, cancellationToken)
            .ConfigureAwait(false);

        if (!roomExists)
        {
            // Checked first so an unknown room costs one existence query, never a history read.
            return Result.Failure<IReadOnlyList<MessageDto>>(ChatRoomErrors.NotFound);
        }

        IReadOnlyList<MessageDto> latest = await messages
            .GetLatestAsync(request.ChatRoomId, request.Count, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(latest);
    }
}
