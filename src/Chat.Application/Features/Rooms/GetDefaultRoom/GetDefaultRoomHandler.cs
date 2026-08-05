using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Contracts.Rooms;
using Chat.Application.Errors;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;

namespace Chat.Application.Features.Rooms.GetDefaultRoom;

/// <summary>
/// Serves <see cref="GetDefaultRoomQuery"/>: look the seeded room up by name and hand back the
/// projection.
/// </summary>
/// <remarks>
/// This exists so the chat page never touches the <c>DbContext</c>. The page is a host concern and the
/// database is an infrastructure one; a room identifier reaches the browser through the same use-case
/// pipeline as everything else, which is what keeps the dependency rule true rather than merely stated.
/// </remarks>
/// <param name="chatRooms">The read side of the room store.</param>
internal sealed class GetDefaultRoomHandler(IChatRoomRepository chatRooms)
    : IQueryHandler<GetDefaultRoomQuery, ChatRoomDto>, IWebFeature
{
    /// <summary>
    /// The seeded room name as a validated value object. The literal is a compile-time constant well
    /// inside every room-name rule, so a failure here would mean those rules changed underneath both
    /// this query and the seeder — a bug, not an expected outcome.
    /// </summary>
    private static readonly RoomName SeededRoomName = RoomName.Create(ChatRoomConstants.DefaultRoomName).Value;

    public async Task<Result<ChatRoomDto>> Handle(
        GetDefaultRoomQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ChatRoomDto? room = await chatRooms
            .FindByNameAsync(SeededRoomName, cancellationToken)
            .ConfigureAwait(false);

        return room is null
            // The seeder runs at startup, so an absent room means the database was never initialised.
            // The page reports it and offers no connection, which beats joining a room that cannot exist.
            ? Result.Failure<ChatRoomDto>(ChatRoomErrors.NotFound)
            : Result.Success(room);
    }
}
