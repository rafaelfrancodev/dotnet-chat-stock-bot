using System.Text.Json;
using Chat.Application.Contracts.Messaging;
using MassTransit.Serialization;

namespace Chat.UnitTests.Infrastructure.Messaging;

/// <summary>
/// The two wire contracts cross a process boundary, so their JSON shape is part of the deliverable.
/// These tests use the exact <see cref="JsonSerializerOptions"/> MassTransit puts on the wire, which is
/// why a change to the contracts — a renamed member, a reordered enum, a lost converter — fails here
/// rather than as a message silently landing in <c>stock-quote-requests_skipped</c> at run time.
/// </summary>
public sealed class StockQuoteContractSerializationTests
{
    private static readonly JsonSerializerOptions WireOptions = SystemTextJsonMessageSerializer.Options;

    private static readonly StockQuoteRequested Request = new(
        RequestId: Guid.Parse("0198cd4d-0000-7000-8000-00000000abcd"),
        ChatRoomId: Guid.Parse("0198cd4d-1111-7000-8000-00000000abcd"),
        StockCode: "aapl.us",
        RequestedByUserId: "user-42",
        RequestedByDisplayName: "Ada",
        RequestedAtUtc: new DateTimeOffset(2026, 8, 4, 21, 15, 30, TimeSpan.Zero));

    private static readonly StockQuoteResolved Response = new(
        RequestId: Request.RequestId,
        ChatRoomId: Request.ChatRoomId,
        StockCode: "aapl.us",
        RequestedByUserId: Request.RequestedByUserId,
        Outcome: StockQuoteOutcome.Quoted,
        Price: 93.42m,
        Message: "AAPL.US quote is $93.42 per share",
        ResolvedAtUtc: new DateTimeOffset(2026, 8, 4, 21, 15, 31, TimeSpan.Zero));

    public static TheoryData<object> Contracts() => new() { Request, Response };

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Serializer_RoundTrip_PreservesContract(object contract)
    {
        string json = JsonSerializer.Serialize(contract, contract.GetType(), WireOptions);

        object? roundTripped = JsonSerializer.Deserialize(json, contract.GetType(), WireOptions);

        roundTripped.Should().Be(contract);
    }

    [Theory]
    [InlineData(StockQuoteOutcome.Quoted)]
    [InlineData(StockQuoteOutcome.SymbolNotFound)]
    [InlineData(StockQuoteOutcome.LookupFailed)]
    public void Serializer_RoundTrip_PreservesTheOutcome(StockQuoteOutcome outcome)
    {
        StockQuoteResolved resolved = Response with { Outcome = outcome };

        string json = JsonSerializer.Serialize(resolved, WireOptions);

        JsonSerializer.Deserialize<StockQuoteResolved>(json, WireOptions)!.Outcome.Should().Be(outcome);
    }

    /// <summary>
    /// The outcome travels as its name, not its ordinal, so inserting a member into the enum can never
    /// re-interpret messages already sitting in a queue while the two hosts are deployed apart.
    /// </summary>
    [Fact]
    public void Serializer_Outcome_IsWrittenAsItsNameNotItsOrdinal()
    {
        string json = JsonSerializer.Serialize(Response with { Outcome = StockQuoteOutcome.SymbolNotFound }, WireOptions);

        json.Should().Contain("\"SymbolNotFound\"").And.NotContain("\"outcome\":1");
    }

    /// <summary>
    /// A missing price is the normal shape of a failed lookup, so it must survive as null rather than
    /// collapsing to zero — a bot answer reading "$0.00 per share" would be worse than no answer.
    /// </summary>
    [Fact]
    public void Serializer_RoundTrip_KeepsAnAbsentPriceAbsent()
    {
        StockQuoteResolved resolved = Response with
        {
            Outcome = StockQuoteOutcome.SymbolNotFound,
            Price = null,
            Message = "Sorry, I could not find a quote for AAPL.US.",
        };

        string json = JsonSerializer.Serialize(resolved, WireOptions);

        JsonSerializer.Deserialize<StockQuoteResolved>(json, WireOptions)!.Price.Should().BeNull();
    }

    /// <summary>
    /// The price is the one member where a formatting slip becomes a wrong number rather than a crash.
    /// </summary>
    [Fact]
    public void Serializer_RoundTrip_PreservesThePriceExactly()
    {
        StockQuoteResolved resolved = Response with { Price = 1234.05m };

        string json = JsonSerializer.Serialize(resolved, WireOptions);

        JsonSerializer.Deserialize<StockQuoteResolved>(json, WireOptions)!.Price.Should().Be(1234.05m);
    }

    /// <summary>
    /// The answer carries the requester's id so Chat.Web can aim the outage banner at one participant.
    /// If it were dropped on the wire the alert would silently have nowhere to go — and
    /// <c>Clients.User(null)</c> fails at the hub, not here, which is a much worse place to find out.
    /// </summary>
    [Fact]
    public void Serializer_RoundTrip_PreservesTheRequesterTheAlertIsAimedAt()
    {
        string json = JsonSerializer.Serialize(Response, WireOptions);

        JsonSerializer.Deserialize<StockQuoteResolved>(json, WireOptions)!
            .RequestedByUserId.Should().Be(Request.RequestedByUserId);
    }

    /// <summary>
    /// Timestamps order the chat and correlate the two hops, so the instant must not drift through the
    /// serializer.
    /// </summary>
    [Fact]
    public void Serializer_RoundTrip_PreservesTheRequestInstant()
    {
        string json = JsonSerializer.Serialize(Request, WireOptions);

        JsonSerializer.Deserialize<StockQuoteRequested>(json, WireOptions)!
            .RequestedAtUtc.Should().Be(Request.RequestedAtUtc);
    }
}
