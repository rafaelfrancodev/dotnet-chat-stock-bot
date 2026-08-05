namespace Chat.Application.Contracts.Rooms;

/// <summary>
/// A chat room as it crosses the boundary to a client: projected in SQL by the repository and returned
/// by the room queries.
/// </summary>
/// <remarks>
/// Lives in <c>Contracts</c> for the same reason as <c>MessageDto</c>: the repository port and the query
/// result share it, and the multiple-rooms bonus will add a third consumer that lists rooms.
/// <para>
/// The identifier is a <see cref="Guid"/> rather than a <c>ChatRoomId</c> because this record is what a
/// page or a client sees; the strongly-typed id stays inside the server.
/// </para>
/// </remarks>
/// <param name="Id">Identity of the room, rendered into the page and sent back on every hub call.</param>
/// <param name="Name">Name of the room. Rendered as text, never as HTML.</param>
public sealed record ChatRoomDto(Guid Id, string Name);
