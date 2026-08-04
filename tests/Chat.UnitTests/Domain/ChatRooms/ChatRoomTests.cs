using System.Reflection;
using Chat.Domain.ChatRooms;
using Chat.Domain.Common;
using Chat.Domain.Messages;

namespace Chat.UnitTests.Domain.ChatRooms;

public sealed class ChatRoomTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 4, 10, 30, 0, TimeSpan.Zero);

    private static RoomName Name => RoomName.Create("General Chat").Value;

    [Fact]
    public void Create_ValidName_RaisesChatRoomCreated()
    {
        Result<ChatRoom> result = ChatRoom.Create(Name, CreatedAt);

        result.IsSuccess.Should().BeTrue();
        ChatRoom room = result.Value;
        room.Name.Should().Be(Name);
        room.CreatedAtUtc.Should().Be(CreatedAt);
        room.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<ChatRoomCreated>();
    }

    [Fact]
    public void Create_ValidName_RaisesExactlyOneEventCarryingIdAndName()
    {
        ChatRoom room = ChatRoom.Create(Name, CreatedAt).Value;

        ChatRoomCreated created = room.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<ChatRoomCreated>().Subject;
        created.ChatRoomId.Should().Be(room.Id);
        created.Name.Should().Be(Name);
        created.OccurredAtUtc.Should().Be(room.CreatedAtUtc);
    }

    [Fact]
    public void Create_NullName_Throws()
    {
        Action create = () => ChatRoom.Create(null!, CreatedAt);

        create.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_DefaultCreationTime_ReturnsFailure()
    {
        Result<ChatRoom> result = ChatRoom.Create(Name, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(ChatRoom.Errors.MissingCreationTime);
    }

    [Fact]
    public void Create_NonUtcCreationTime_NormalisesToUtcWithoutChangingTheInstant()
    {
        DateTimeOffset localTime = new(2026, 8, 4, 12, 30, 0, TimeSpan.FromHours(2));

        ChatRoom room = ChatRoom.Create(Name, localTime).Value;

        room.CreatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        room.CreatedAtUtc.Should().Be(localTime);
    }

    [Fact]
    public void Create_TwoRooms_GetDistinctIdentities()
    {
        ChatRoom first = ChatRoom.Create(Name, CreatedAt).Value;
        ChatRoom second = ChatRoom.Create(Name, CreatedAt).Value;

        first.Id.Should().NotBe(second.Id);
        first.Should().NotBe(second);
    }

    [Fact]
    public void ClearDomainEvents_AfterCreate_RemovesTheRecordedEvent()
    {
        ChatRoom room = ChatRoom.Create(Name, CreatedAt).Value;

        room.ClearDomainEvents();

        room.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Constructors_AreAllNonPublic_SoTheFactoryIsTheOnlyEntryPoint()
    {
        ConstructorInfo[] constructors = typeof(ChatRoom)
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        constructors.Should().NotBeEmpty();
        constructors.Should().OnlyContain(constructor => constructor.IsPrivate);
    }

    [Fact]
    public void ChatRoom_HoldsNoMessages_SoTheAggregateBoundaryIsPreserved()
    {
        const BindingFlags members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        IEnumerable<Type> memberTypes = typeof(ChatRoom).GetProperties(members)
            .Select(property => property.PropertyType)
            .Concat(typeof(ChatRoom).GetFields(members).Select(field => field.FieldType));

        memberTypes.Should().NotContain(type => ReferencesMessages(type));
    }

    private static bool ReferencesMessages(Type type) =>
        type == typeof(Message) || Array.Exists(type.GetGenericArguments(), argument => argument == typeof(Message));
}
