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
    const alertBox = document.getElementById("chat-alert");
    const alertText = document.getElementById("chat-alert-text");
    const alertDismiss = document.getElementById("chat-alert-dismiss");
    const help = document.getElementById("chat-help");
    const helpDismiss = document.getElementById("chat-help-dismiss");

    // Error = 2 in ChatAlertSeverity. Anything else is a warning and is rendered amber.
    const SEVERITY_ERROR = 2;

    const HELP_DISMISSED = "chat.help.dismissed";

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

    function hideAlert() {
        alertBox.classList.add("d-none");
        alertText.textContent = "";
    }

    // A system condition, not a post: it never enters the message list, so it cannot scroll away and is
    // not part of the last-50 history. Text is curated server-side but still written with textContent.
    function showAlert(alert) {
        if (!alert || !alert.message) {
            return;
        }

        alertText.textContent = alert.message;
        alertBox.classList.toggle("alert-danger", alert.severity === SEVERITY_ERROR);
        alertBox.classList.toggle("alert-warning", alert.severity !== SEVERITY_ERROR);
        alertBox.classList.remove("d-none");
    }

    alertDismiss.addEventListener("click", hideAlert);

    // Shown until dismissed once. localStorage rather than a cookie: it is a display preference, nothing
    // the server needs, and reading it must never stop the chat from loading.
    function setUpHelp() {
        let dismissed = false;

        try {
            dismissed = window.localStorage.getItem(HELP_DISMISSED) === "true";
        } catch (error) {
            console.warn("Help preference unavailable; showing the help panel.", error);
        }

        help.classList.toggle("d-none", dismissed);

        helpDismiss.addEventListener("click", () => {
            help.classList.add("d-none");

            try {
                window.localStorage.setItem(HELP_DISMISSED, "true");
            } catch (error) {
                console.warn("Help preference could not be saved.", error);
            }
        });
    }

    /**
     * Appends a line that exists only in this window: the command the participant typed, or an error the
     * server sent them alone. Neither is a post — nothing here is stored, broadcast, or part of the
     * last-50 history, and all of it disappears on reload. That is what keeps a `/stock=` command out of
     * the database while still letting the person who typed it see that something happened.
     */
    function renderLocal(label, text, className) {
        const marker = document.createElement("em");
        marker.className = "text-muted me-2";
        marker.textContent = label;

        const content = document.createElement("span");
        content.className = className;
        content.textContent = text;

        const item = document.createElement("li");
        item.className = "small";
        item.append(marker, content);

        messages.appendChild(item);
        messages.scrollTop = messages.scrollHeight;
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
    // They go into the message list, not just the status line: an unknown command or a rejected ticker
    // used to leave the list unchanged, which reads as "nothing happened" rather than "that was refused".
    connection.on("ReceiveError", (message) => renderLocal("not sent —", message, "text-danger"));

    // Sent to this participant's connections only — for example when Stooq is unreachable.
    connection.on("ReceiveAlert", showAlert);

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

        // A command is echoed locally; an ordinary message is not. The difference is that an ordinary
        // message comes back through the room broadcast — which includes this connection — so echoing it
        // would show it twice, while a command is never broadcast and never stored, so without this the
        // participant sees no evidence they typed anything at all.
        if (text.startsWith("/")) {
            renderLocal("you typed —", text, "text-muted");
        }

        try {
            await connection.invoke("SendMessage", roomId, text);
        } catch (error) {
            renderLocal("not sent —", "The message could not be sent. Check the connection status below.", "text-danger");
            console.error(error);
        }
    });

    (async function start() {
        setUpHelp();
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
