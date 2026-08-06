using Chat.Application.Contracts.Messages;
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
    public static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(15);

    private readonly HubConnection connection;
    private readonly Lock gate = new();
    private readonly Queue<MessageDto> messages = new();
    private readonly List<string> errors = [];

    private TaskCompletionSource<MessageDto>? pendingMessage;

    private TestHubClient(HubConnection connection)
    {
        this.connection = connection;

        connection.On<MessageDto>(ChatHub.ReceiveMessage, Push);
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
        await connection.StartAsync().ConfigureAwait(false);

        return client;
    }

    /// <summary>Joins a room and returns the history the chat window opens with, oldest first.</summary>
    /// <param name="chatRoomId">Room to join.</param>
    public Task<IReadOnlyList<MessageDto>> JoinRoomAsync(Guid chatRoomId) =>
        connection.InvokeAsync<IReadOnlyList<MessageDto>>(nameof(ChatHub.JoinRoom), chatRoomId);

    /// <summary>Sends one line of chat input, exactly as the page's send box does.</summary>
    /// <param name="chatRoomId">Room to post into.</param>
    /// <param name="text">What the participant typed — a message or a <c>/stock=</c> command.</param>
    public Task SendMessageAsync(Guid chatRoomId, string text) =>
        connection.InvokeAsync(nameof(ChatHub.SendMessage), chatRoomId, text);

    /// <summary>
    /// The next post pushed to this connection, waiting for it if it has not arrived yet.
    /// </summary>
    /// <exception cref="TimeoutException">Nothing arrived within <see cref="PushTimeout"/>.</exception>
    public async Task<MessageDto> NextMessageAsync()
    {
        TaskCompletionSource<MessageDto> waiter;

        lock (gate)
        {
            if (messages.TryDequeue(out MessageDto? alreadyHere))
            {
                return alreadyHere;
            }

            waiter = new TaskCompletionSource<MessageDto>(TaskCreationOptions.RunContinuationsAsynchronously);
            pendingMessage = waiter;
        }

        using CancellationTokenSource timeout = new(PushTimeout);
        using CancellationTokenRegistration registration = timeout.Token.Register(
            () => waiter.TrySetException(new TimeoutException(
                $"No message reached the client within {PushTimeout.TotalSeconds:0} seconds.")));

        return await waiter.Task.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await connection.DisposeAsync().ConfigureAwait(false);

    private void Push(MessageDto message)
    {
        TaskCompletionSource<MessageDto>? waiter;

        lock (gate)
        {
            waiter = pendingMessage;
            pendingMessage = null;

            if (waiter is null)
            {
                messages.Enqueue(message);
            }
        }

        waiter?.TrySetResult(message);
    }

    private void Record(string error)
    {
        lock (gate)
        {
            errors.Add(error);
        }
    }
}
