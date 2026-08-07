using Chat.Application.Abstractions.Hosting;
using Chat.Application.Abstractions.Messaging;
using Chat.Application.Abstractions.Persistence;
using Chat.Application.Abstractions.Realtime;
using Chat.Application.Abstractions.Time;
using Chat.Application.Contracts.Rooms;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;

namespace Chat.Application.Features.Rooms.CreateRoom;

/// <summary>
/// Serves <see cref="CreateRoomCommand"/>: validate the name, reject a duplicate, create the room, commit
/// once, then announce it.
/// </summary>
/// <remarks>
/// The order matters. The name is turned into a <see cref="RoomName"/> first, so the duplicate check runs
/// against the <i>normalised</i> value — checking the raw text would let <c>"General "</c> past a lookup
/// for <c>"General"</c> and leave the unique index to reject it as an exception instead of a sentence.
/// <para>
/// <b>Announced strictly after the commit</b>, for the same reason <c>PostMessageHandler</c> broadcasts
/// after saving: a room offered to other windows but never stored is a room they cannot join.
/// </para>
/// <para>
/// <b>One narrow race is accepted and not hidden.</b> Two participants submitting the same new name in the
/// same instant can both pass the duplicate check; the unique index on <c>ChatRooms.Name</c> then rejects
/// the loser, whose window shows the transport's generic failure rather than "that name is taken". The
/// invariant that matters — one room per name — is held by the database either way. Translating it into
/// the friendly message would mean catching a <c>DbUpdateException</c> here, and this layer must not know
/// what EF Core is; the alternative, pushing the whole use case behind a repository method, would move
/// business rules into infrastructure to improve the wording of a collision nobody has observed.
/// </para>
/// </remarks>
/// <param name="chatRooms">Duplicate lookup and the staging call. Staging only — nothing is written here.</param>
/// <param name="unitOfWork">Commits the room. Called exactly once, and never on a failed path.</param>
/// <param name="notifier">Announces the room to open windows, after the commit.</param>
/// <param name="clock">Supplies the creation time; the domain never reads the clock itself.</param>
internal sealed class CreateRoomHandler(
    IChatRoomRepository chatRooms,
    IUnitOfWork unitOfWork,
    IChatRoomNotifier notifier,
    IDateTimeProvider clock)
    : ICommandHandler<CreateRoomCommand, ChatRoomDto>, IWebFeature
{
    public async Task<Result<ChatRoomDto>> Handle(CreateRoomCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<RoomName> name = RoomName.Create(request.Name);
        if (name.IsFailure)
        {
            return Result.Failure<ChatRoomDto>(name.Error);
        }

        // Against the normalised name, so the check and the unique index agree on what "the same name" is.
        ChatRoomDto? existing = await chatRooms
            .FindByNameAsync(name.Value, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return Result.Failure<ChatRoomDto>(CreateRoomCommand.Errors.NameTaken);
        }

        Result<ChatRoom> room = ChatRoom.Create(name.Value, clock.UtcNow);
        if (room.IsFailure)
        {
            return Result.Failure<ChatRoomDto>(room.Error);
        }

        chatRooms.Add(room.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ChatRoomDto created = new(room.Value.Id.Value, room.Value.Name.Value);
        await notifier.RoomCreatedAsync(created, cancellationToken).ConfigureAwait(false);

        return Result.Success(created);
    }
}
