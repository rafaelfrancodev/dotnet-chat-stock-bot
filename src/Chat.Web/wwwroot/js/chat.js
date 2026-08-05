// The whole chat window: connect, load the last 50 posts, append live ones, send a line.
// Vanilla JS over the vendored SignalR client — no framework and no build step.
//
// Two rules this file must never break:
//   1. Message text is untrusted. Every field is written with textContent, so a post containing markup
//      is displayed, never executed. There is no innerHTML anywhere in this file.
//   2. The room and the author are the server's business. The room comes from the data attribute the
//      page rendered, the author from the authentication cookie, so a client cannot post as somebody
//      else or into a room it did not join.
(function () {
    "use strict";

    const chat = document.getElementById("chat");
    if (!chat) {
        // No room to join (the page said so); there is nothing to connect to.
        return;
    }

    const roomId = chat.dataset.roomId;
    const messages = document.getElementById("chat-messages");
    const form = document.getElementById("chat-form");
    const input = document.getElementById("chat-input");
    const status = document.getElementById("chat-status");

    // JoinRoom subscribes before it reads the history, so a post committed in between legitimately
    // arrives twice. Ids are what settle it: a duplicate is dropped, and nothing is ever lost.
    const rendered = new Set();

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(chat.dataset.hubUrl)
        .withAutomaticReconnect()
        .build();

    function setStatus(text) {
        status.textContent = text;
    }

    function render(message) {
        if (!message || rendered.has(message.id)) {
            return;
        }

        rendered.add(message.id);

        const time = document.createElement("time");
        time.dateTime = message.postedAtUtc;
        time.className = "text-muted me-2";
        time.textContent = new Date(message.postedAtUtc).toLocaleTimeString();

        const author = document.createElement("strong");
        author.textContent = message.authorDisplayName;

        const content = document.createElement("span");
        content.textContent = message.content;

        const item = document.createElement("li");
        item.append(time, author, document.createTextNode(": "), content);

        messages.appendChild(item);
        messages.scrollTop = messages.scrollHeight;
    }

    // The history is already oldest to newest and already capped at 50 by the server. Do not re-sort it
    // and do not ask for a count: the query defaults and caps at the challenge limit on its own.
    async function joinRoom() {
        const history = await connection.invoke("JoinRoom", roomId);
        history.forEach(render);
        setStatus("Connected.");
    }

    connection.on("ReceiveMessage", render);

    // Errors are sent to the caller alone and carry curated text only, so they are safe to show as-is.
    connection.on("ReceiveError", setStatus);

    connection.onreconnecting(() => setStatus("Reconnecting…"));
    connection.onclose(() => setStatus("Disconnected. Reload the page to reconnect."));

    // SignalR does not restore group membership after a reconnect and the server keeps no map to restore
    // it from, so the client re-joins. The same call returns the history, filling whatever it missed.
    connection.onreconnected(async () => {
        try {
            await joinRoom();
        } catch (error) {
            setStatus("Reconnected, but the room could not be rejoined. Reload the page.");
            console.error(error);
        }
    });

    form.addEventListener("submit", async (event) => {
        event.preventDefault();

        const text = input.value.trim();
        if (text.length === 0) {
            return;
        }

        input.value = "";

        try {
            // Nothing is rendered here on purpose: an ordinary post comes back through the room
            // broadcast, which includes this connection, and a /stock= command is answered later by the
            // bot. Echoing either would show it twice.
            await connection.invoke("SendMessage", roomId, text);
        } catch (error) {
            setStatus("The message could not be sent.");
            console.error(error);
        }
    });

    (async function start() {
        setStatus("Connecting…");

        try {
            await connection.start();
            await joinRoom();
            input.focus();
        } catch (error) {
            setStatus("Could not connect to the chat. Reload the page to try again.");
            console.error(error);
        }
    })();
})();
