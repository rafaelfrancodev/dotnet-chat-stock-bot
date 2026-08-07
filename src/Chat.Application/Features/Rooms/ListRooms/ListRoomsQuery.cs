using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Rooms;

namespace Chat.Application.Features.Rooms.ListRooms;

/// <summary>
/// Reads every room a participant can join, ordered by name.
/// </summary>
/// <remarks>
/// It takes no parameter, and that is the access control: every signed-in participant sees the same
/// directory, so there is nothing for a caller to filter, page or widen. A room's <i>contents</i> still
/// require joining its group — this query hands out names and identifiers only.
/// </remarks>
public sealed record ListRoomsQuery : IQuery<IReadOnlyList<ChatRoomDto>>;
