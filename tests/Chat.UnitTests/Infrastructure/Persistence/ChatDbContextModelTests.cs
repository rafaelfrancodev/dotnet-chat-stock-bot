using Chat.Domain.ChatRooms;
using Chat.Domain.Messages;
using Chat.Infrastructure.Identity;
using Chat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Chat.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Pins the mapping decisions of task 1.7 to the model EF Core actually builds. These are metadata
/// assertions, not database round-trips: they need no container, run in milliseconds, and still fail
/// the moment a configuration change would break a bot post, the "last 50" query or an aggregate's
/// materialisation.
/// </summary>
public sealed class ChatDbContextModelTests : IDisposable
{
    /// <summary>
    /// Identity's own <c>AspNetUsers.Id</c> width. Spelled out rather than read from the internal
    /// constant so that widening a column stays a deliberate, reviewed change.
    /// </summary>
    private const int ExpectedUserIdMaxLength = 450;

    /// <summary>Identity's <c>UserName</c> width, and therefore every display-name column's.</summary>
    private const int ExpectedDisplayNameMaxLength = 256;

    private readonly ChatDbContext _context = TestDatabase.CreateContext();
    private readonly IModel _model;

    public ChatDbContextModelTests() => _model = TestDatabase.ModelOf(_context);

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Model_ForChatAndIdentity_BuildsWithoutThrowing()
    {
        _model.FindEntityType(typeof(Message)).Should().NotBeNull();
        _model.FindEntityType(typeof(ChatRoom)).Should().NotBeNull();
        _model.FindEntityType(typeof(ApplicationUser)).Should().NotBeNull();
    }

    [Fact]
    public void Messages_HaveNoForeignKey_SoTheBotCanOwnItsPosts()
    {
        IEntityType messages = MessagesEntityType();

        messages.GetForeignKeys().Should().BeEmpty(
            "the author is stored as a plain user id — the bot's \"system:bot\" is not an Identity user, " +
            "so a foreign key to AspNetUsers would reject every quote answer — and the room is a " +
            "cross-aggregate reference validated by IChatRoomRepository.ExistsAsync");
    }

    [Fact]
    public void Messages_AreIndexedByRoomThenNewestPostFirst()
    {
        IIndex index = MessagesEntityType().GetIndexes().Single();

        index.GetDatabaseName().Should().Be("IX_Messages_ChatRoomId_PostedAtUtc");
        index.Properties.Select(property => property.Name)
            .Should().Equal(nameof(Message.ChatRoomId), nameof(Message.PostedAtUtc));
        index.IsDescending.Should().Equal(false, true);
        index.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void ChatRooms_HaveAUniqueIndexOnName()
    {
        IIndex index = ChatRoomsEntityType().GetIndexes().Single();

        index.GetDatabaseName().Should().Be("IX_ChatRooms_Name");
        index.Properties.Select(property => property.Name).Should().Equal(nameof(ChatRoom.Name));
        index.IsUnique.Should().BeTrue();
    }

    [Theory]
    [InlineData(typeof(Message), nameof(Message.Id))]
    [InlineData(typeof(Message), nameof(Message.ChatRoomId))]
    [InlineData(typeof(ChatRoom), nameof(ChatRoom.Id))]
    public void StronglyTypedIds_AreStoredAsGuids(Type aggregate, string propertyName)
    {
        IProperty property = PropertyOf(aggregate, propertyName);

        property.GetValueConverter().Should().NotBeNull();
        property.GetValueConverter()!.ProviderClrType.Should().Be<Guid>();
        property.GetColumnType().Should().Be("uniqueidentifier");
    }

    [Theory]
    [InlineData(typeof(Message), nameof(Message.Content), MessageConstants.MaxContentLength)]
    [InlineData(typeof(ChatRoom), nameof(ChatRoom.Name), RoomName.MaxLength)]
    public void ValueObjects_AreStoredAsBoundedStrings(Type aggregate, string propertyName, int maxLength)
    {
        IProperty property = PropertyOf(aggregate, propertyName);

        property.GetValueConverter().Should().NotBeNull();
        property.GetValueConverter()!.ProviderClrType.Should().Be<string>();
        property.GetMaxLength().Should().Be(maxLength);
        property.IsNullable.Should().BeFalse();
    }

    [Fact]
    public void MessageAuthor_IsStoredInTheMessagesTable_AsTwoBoundedColumns()
    {
        IComplexProperty author = MessagesEntityType().GetComplexProperties().Single();

        IProperty userId = author.ComplexType.GetProperties().Single(p => p.Name == nameof(MessageAuthor.UserId));
        IProperty displayName =
            author.ComplexType.GetProperties().Single(p => p.Name == nameof(MessageAuthor.DisplayName));

        userId.GetColumnName().Should().Be("AuthorUserId");
        userId.GetMaxLength().Should().Be(ExpectedUserIdMaxLength);
        displayName.GetColumnName().Should().Be("AuthorDisplayName");
        displayName.GetMaxLength().Should().Be(ExpectedDisplayNameMaxLength);
    }

    [Fact]
    public void MessageOrigin_IsStoredAsAnInteger()
    {
        IProperty origin = PropertyOf(typeof(Message), nameof(Message.Origin));

        origin.GetProviderClrType().Should().Be<int>();
        origin.GetColumnType().Should().Be("int");
    }

    [Theory]
    [InlineData(typeof(Message), nameof(Message.PostedAtUtc))]
    [InlineData(typeof(ChatRoom), nameof(ChatRoom.CreatedAtUtc))]
    public void Timestamps_AreStoredAsUtcInstantsWithoutAnOffset(Type aggregate, string propertyName)
    {
        IProperty property = PropertyOf(aggregate, propertyName);

        property.GetValueConverter().Should().NotBeNull();
        property.GetValueConverter()!.ProviderClrType.Should().Be<DateTime>();
        property.GetColumnType().Should().Be("datetime2(7)");
    }

    [Fact]
    public void ReadingATimestamp_RestoresUtcKindAndZeroOffset()
    {
        IProperty property = PropertyOf(typeof(Message), nameof(Message.PostedAtUtc));
        DateTime stored = new(2026, 8, 4, 12, 30, 0, DateTimeKind.Unspecified);

        object? restored = property.GetValueConverter()!.ConvertFromProvider(stored);

        restored.Should().BeOfType<DateTimeOffset>()
            .Which.Should().Be(new DateTimeOffset(2026, 8, 4, 12, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(typeof(Message))]
    [InlineData(typeof(ChatRoom))]
    public void Aggregates_DoNotPersistTheirDomainEvents(Type aggregate)
    {
        IEntityType entityType = _model.FindEntityType(aggregate)!;

        entityType.FindProperty("DomainEvents").Should().BeNull();
        entityType.GetNavigations().Should().BeEmpty();
    }

    [Fact]
    public void IdentityUsers_CarryABoundedDisplayName()
    {
        IProperty displayName = PropertyOf(typeof(ApplicationUser), nameof(ApplicationUser.DisplayName));

        displayName.GetMaxLength().Should().Be(ExpectedDisplayNameMaxLength);
        displayName.IsNullable.Should().BeFalse(
            "the value is copied into Messages.AuthorDisplayName on every post");
    }

    private IEntityType MessagesEntityType() => _model.FindEntityType(typeof(Message))!;

    private IEntityType ChatRoomsEntityType() => _model.FindEntityType(typeof(ChatRoom))!;

    private IProperty PropertyOf(Type aggregate, string propertyName) =>
        _model.FindEntityType(aggregate)!.FindProperty(propertyName)!;
}
