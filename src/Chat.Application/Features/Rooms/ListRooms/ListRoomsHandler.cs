using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Rooms;
using Chat.Domain.Common;

namespace Chat.Application.Features.Rooms.ListRooms;

/// <summary>
/// Serves <see cref="ListRoomsQuery"/>: hand back the room directory as projected by the repository.
/// </summary>
/// <remarks>
/// A pass-through by design. There is no rule to apply to "which rooms exist" — every participant may see
/// all of them — so adding a filter here would be inventing policy the challenge does not ask for. An
/// empty list is a success, not a failure: it means seeding has not run, which the page renders as an
/// explanation rather than an error.
/// </remarks>
/// <param name="chatRooms">The read side of the room store.</param>
internal sealed class ListRoomsHandler(IChatRoomRepository chatRooms)
    : IQueryHandler<ListRoomsQuery, IReadOnlyList<ChatRoomDto>>, IWebFeature
{
    public async Task<Result<IReadOnlyList<ChatRoomDto>>> Handle(
        ListRoomsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<ChatRoomDto> rooms = await chatRooms
            .ListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(rooms);
    }
}
