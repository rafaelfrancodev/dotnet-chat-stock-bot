using Chat.Application.Contracts.Messages;
using Chat.Application.Contracts.Rooms;
using Chat.Web.Hubs;
using Microsoft.AspNetCore.SignalR.Client;

namespace Chat.IntegrationTests.Infrastructure;

/// <summary>
/// A connected SignalR client that records what the server pushed to it, and lets a test wait for the next
/// push without sleeping.
/// </summary>
/// <remarks>
/// <b>Every wait is a <see cref="TaskCompletionSource{TResult}"/> with a timeout.</b> Polling with
/// <c>Task.Delay</c> would either make the suite slow or make it flaky, and would report "no message
/// arrived" for a message that arrived a millisecond late. A push either completes the pending waiter or is
/// queued for the next one, so a test can await messages in the order the server sent them even when they
/// arrive before it asks.
/// </remarks>
public sealed class TestHubClient : IAsyncDisposable
{
    /// <summary>
    /// How long a test waits for a push before failing. Generous enough for a cold in-memory host, short
    /// enough that a broken broadcast fails the suite in seconds.
    /// </summary>
    public static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Upper bound on one hub invocation.
    /// </summary>
    /// <remarks>
    /// <c>HubConnection.InvokeAsync</c> has no timeout of its own, so without this a stalled call consumed
    /// the whole per-test budget and surfaced as an unattributed "test exceeded 60 seconds" — which says
    /// nothing about which call hung. Bounding each invocation turns that into a named failure.
    /// </remarks>
    public static readonly TimeSpan InvokeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long to wait when the expectation is that <b>nothing</b> arrives. Long enough that a broadcast
    /// which should have been suppressed has time to show up and fail the test, short enough that several
    /// such assertions do not dominate the suite.
    /// </summary>
    public static readonly TimeSpan SilenceWindow = TimeSpan.FromSeconds(2);

    private readonly HubConnection connection;
    private readonly Lock gate = new();
    private readonly PushChannel<MessageDto> messages = new();
    private readonly PushChannel<ChatRoomDto> rooms = new();
    private readonly List<string> errors = [];

    private TestHubClient(HubConnection connection)
    {
        this.connection = connection;

        connection.On<MessageDto>(ChatHub.ReceiveMessage, messages.Push);
        connection.On<ChatRoomDto>(ChatHub.ReceiveRoom, rooms.Push);
        connection.On<string>(ChatHub.ReceiveError, Record);
    }

    /// <summary>Errors the server sent to this caller alone, in arrival order.</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (gate)
            {
                return [.. errors];
            }
        }
    }

    /// <summary>Starts the connection and wraps it.</summary>
    /// <param name="connection">A connection built for one participant; ownership passes to this client.</param>
    public static async Task<TestHubClient> StartAsync(HubConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        TestHubClient client = new(connection);

        // Bounded like every other wait here. This was the last unbounded await in the hub path, and an
        // unbounded one is how a stalled negotiate turned into "the test exceeded its time limit" with no
        // indication of where it stopped.
        using CancellationTokenSource timeout = new(InvokeTimeout);

        try
        {
            await connection.StartAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The hub connection did not start within {InvokeTimeout.TotalSeconds:0} seconds. "
                + "The negotiate or the long-polling connect stalled.",
                exception);
        }

        return client;
    }

    /// <summary>Joins a room and returns the history the chat window opens with, oldest first.</summary>
    /// <param name="chatRoomId">Room to join.</param>
    public Task<IReadOnlyList<MessageDto>> JoinRoomAsync(Guid chatRoomId) =>
        InvokeAsync(
            token => connection.InvokeAsync<IReadOnlyList<MessageDto>>(nameof(ChatHub.JoinRoom), chatRoomId, token),
            nameof(ChatHub.JoinRoom));

    /// <summary>Sends one line of chat input, exactly as the page's send box does.</summary>
    /// <param name="chatRoomId">Room to post into.</param>
    /// <param name="text">What the participant typed — a message or a <c>/stock=</c> command.</param>
    public async Task SendMessageAsync(Guid chatRoomId, string text) =>
        await InvokeAsync<object?>(
            async token =>
            {
                await connection.InvokeAsync(nameof(ChatHub.SendMessage), chatRoomId, text, token)
                    .ConfigureAwait(false);
                return null;
            },
            nameof(ChatHub.SendMessage)).ConfigureAwait(false);

    /// <summary>Creates a room, exactly as the page's "create and join" box does.</summary>
    /// <param name="name">The name to request. Untrusted and unnormalised, as it is from a browser.</param>
    /// <returns>The created room, or <see langword="null"/> when the server refused the name.</returns>
    public Task<ChatRoomDto?> CreateRoomAsync(string name) =>
        InvokeAsync(
            token => connection.InvokeAsync<ChatRoomDto?>(nameof(ChatHub.CreateRoom), name, token),
            nameof(ChatHub.CreateRoom));

    /// <summary>
    /// The next post pushed to this connection, waiting for it if it has not arrived yet.
    /// </summary>
    /// <exception cref="TimeoutException">Nothing arrived within <see cref="PushTimeout"/>.</exception>
    public async Task<MessageDto> NextMessageAsync() =>
        await messages.TryNextAsync(PushTimeout).ConfigureAwait(false)
        ?? throw new TimeoutException(
            $"No message reached the client within {PushTimeout.TotalSeconds:0} seconds.");

    /// <summary>
    /// The next post, or <see langword="null"/> if none arrives within <paramref name="within"/>. For
    /// asserting that a broadcast was correctly <i>not</i> delivered — a room this connection did not join.
    /// </summary>
    /// <param name="within">How long to wait. Defaults to <see cref="SilenceWindow"/>.</param>
    public Task<MessageDto?> TryNextMessageAsync(TimeSpan? within = null) =>
        messages.TryNextAsync(within ?? SilenceWindow);

    /// <summary>The next room announcement, waiting for it if it has not arrived yet.</summary>
    /// <exception cref="TimeoutException">Nothing arrived within <see cref="PushTimeout"/>.</exception>
    public async Task<ChatRoomDto> NextRoomAsync() =>
        await rooms.TryNextAsync(PushTimeout).ConfigureAwait(false)
        ?? throw new TimeoutException(
            $"No room announcement reached the client within {PushTimeout.TotalSeconds:0} seconds.");

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await connection.DisposeAsync().ConfigureAwait(false);

    /// <summary>
    /// Runs one hub invocation under <see cref="InvokeTimeout"/> and reports a stall by name.
    /// </summary>
    private static async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> invoke, string hubMethod)
    {
        using CancellationTokenSource timeout = new(InvokeTimeout);

        try
        {
            return await invoke(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"{hubMethod} did not answer within {InvokeTimeout.TotalSeconds:0} seconds. The server "
                + "accepted the connection but never completed the invocation.",
                exception);
        }
    }

    private void Record(string error)
    {
        lock (gate)
        {
            errors.Add(error);
        }
    }

    /// <summary>
    /// One stream of server pushes: a queue for what arrived before anybody asked, and at most one waiter
    /// for what has not arrived yet.
    /// </summary>
    /// <remarks>
    /// A timed-out waiter gives its slot back, so a push that arrives late is queued for the next caller
    /// instead of being handed to a completed <see cref="TaskCompletionSource{TResult}"/> and lost. That
    /// matters as soon as a test is allowed to time out on purpose, which
    /// <see cref="TryNextMessageAsync"/> does.
    /// </remarks>
    private sealed class PushChannel<T>
        where T : class
    {
        private readonly Lock gate = new();
        private readonly Queue<T> received = new();

        private TaskCompletionSource<T>? pending;

        public void Push(T item)
        {
            TaskCompletionSource<T>? waiter;

            lock (gate)
            {
                waiter = pending;
                pending = null;

                if (waiter is null)
                {
                    received.Enqueue(item);
                }
            }

            waiter?.TrySetResult(item);
        }

        public async Task<T?> TryNextAsync(TimeSpan timeout)
        {
            TaskCompletionSource<T> waiter;

            lock (gate)
            {
                if (received.TryDequeue(out T? alreadyHere))
                {
                    return alreadyHere;
                }

                waiter = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = waiter;
            }

            using CancellationTokenSource cancellation = new(timeout);
            using CancellationTokenRegistration registration =
                cancellation.Token.Register(() => waiter.TrySetCanceled());

            try
            {
                return await waiter.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                lock (gate)
                {
                    if (ReferenceEquals(pending, waiter))
                    {
                        pending = null;
                    }
                }

                return null;
            }
        }
    }
}
