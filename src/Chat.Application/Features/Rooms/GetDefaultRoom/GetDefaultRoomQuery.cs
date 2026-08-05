using Chat.Application.Abstractions.Messaging;
using Chat.Application.Contracts.Rooms;
using Chat.Domain.ChatRooms;

namespace Chat.Application.Features.Rooms.GetDefaultRoom;

/// <summary>
/// Reads the room a chat window opens on: the one named
/// <see cref="ChatRoomConstants.DefaultRoomName"/>, created at startup by the seeder.
/// </summary>
/// <remarks>
/// It takes no parameter on purpose. The page needs a room identifier to join, and letting the browser
/// name the room it wants would put a client-supplied lookup key into the read path for no benefit —
/// the challenge has exactly one room, and choosing between several is the multiple-rooms bonus, which
/// will add its own listing query next to this one rather than widening this one.
/// </remarks>
public sealed record GetDefaultRoomQuery : IQuery<ChatRoomDto>;
