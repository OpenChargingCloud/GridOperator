/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of GridOperator <https://github.com/OpenChargingCloud/GridOperator>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Collections.Concurrent;
using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.SSH;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

using cloud.charging.open.protocols.OpenADRv3.Messaging;
using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.OpenADR;
using cloud.charging.open.protocols.WWCP.Node.Streams;
using cloud.charging.open.protocols.WWCP.Node.Connections;
using cloud.charging.open.protocols.WWCP.Node.SecureShell;

using cloud.charging.open.GridOperator.OpenADR;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// The JSON messages of OpenADR - the binding of messages -: the VENs that
    /// speak them over a WebSocket of the subprotocol <c>openadr3.1</c>, or over
    /// the SSH subsystem <c>openadr@open.charging.cloud</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A request is the request of the REST API it stands for</b> - see
    /// <see cref="OpenADRActions"/> -, put to the VTN of the tunnels as one
    /// request of HTTP/1.1 over a stream of its own, which holds the request and
    /// takes the answer, as the VEN the connection names
    /// (<see cref="VENTunnelStream"/>, marked as one of messages). So there is
    /// one VTN, one code for what a VEN may see and do - the object privacy of
    /// OpenADR 3.1, the checks of a report or a subscription -, whichever
    /// binding asks; and the JSON API and the web interface are no more
    /// reached by a message than through a tunnel.
    /// </para>
    /// <para>
    /// <b>A subscription made over a connection of messages is told over it</b>,
    /// by the action Notify: its callback is of the kind of the connection -
    /// ws://, wss:// or ssh://, which the VEN says, conventionally the URL it
    /// connected to -, and stands for the connection it was made over, by the
    /// connection this grid operator wrote down for it. A VEN that connects
    /// again comes in under the same connection, and is told over the new one.
    /// While it is not in, what it would be told waits for it - at most
    /// <see cref="NotificationsKeptPerConnection"/>, each for at most
    /// <see cref="NotificationsKeptFor"/> - and is told in order once it
    /// asked something over the new one.
    /// </para>
    /// </remarks>
    public partial class GridOperator
    {

        #region Data

        /// <summary>
        /// What the log, who and the SSH page call the subsystem of the JSON
        /// messages of OpenADR.
        /// </summary>
        public const String OpenADRMessagesOverSSHLabel = "OpenADR messages";

        /// <summary>
        /// The host a request of a message is asked of: never looked up nor
        /// connected to - there is no connection, only the VTN of the tunnels.
        /// </summary>
        public const String MessagesHost = "vtn.openadr.messages";

        /// <summary>
        /// One connection of messages that is in: the connection it came under,
        /// what of it let it in then, its login, what it came over, its end of
        /// the messages, and how what it goes over is closed.
        /// </summary>
        private sealed record VENMessageSession(ConnectionEntry         Entry,
                                                VENDoor                 Door,
                                                User_Id                 Login,
                                                String                  Transport,
                                                OpenADRMessageEndpoint  Endpoint,
                                                Func<String, Task>      Close);

        /// <summary>
        /// A notification for a connection of messages, waiting to be told, and
        /// since when.
        /// </summary>
        private sealed record WaitingNotification(OpenADRNotification  Notification,
                                                  DateTimeOffset       Since);

        private          SshMessageServer?                                       venMessageServer;

        // By what it came over - the WebSocket connection, the SSH connection
        // of messages - and by the connection of this grid operator it came
        // under, the latest of it.
        private readonly ConcurrentDictionary<Object, VENMessageSession>        venMessageSessions   = new (ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<String, OpenADRMessageEndpoint>   venMessageEndpoints  = new ();

        private          Int64                                                   notificationsTold;

        // By the connection of this grid operator: what waits to be told over
        // it, oldest first - and whether it is being told now.
        private readonly ConcurrentDictionary<String, ConcurrentQueue<WaitingNotification>>  notificationsWaiting  = new ();
        private readonly ConcurrentDictionary<String, Byte>                                  notificationsTelling  = new ();

        // The ends of the messages whose VEN asked something over them: it is
        // ready to be told - not before.
        private readonly ConcurrentDictionary<OpenADRMessageEndpoint, Byte>                  endpointsThatAsked    = new (ReferenceEqualityComparer.Instance);

        #endregion

        #region Properties: how long and how many notifications are kept

        /// <summary>
        /// How many notifications are kept for a connection of messages whose VEN
        /// is not in: beyond them, the oldest is let go of.
        /// </summary>
        public static Int32     NotificationsKeptPerConnection    { get; set; } = 100;

        /// <summary>
        /// How long a notification is kept for a connection of messages whose VEN
        /// is not in.
        /// </summary>
        public static TimeSpan  NotificationsKeptFor              { get; set; } = TimeSpan.FromHours(1);

        #endregion

        #region Properties

        /// <summary>
        /// The connections of VENs in the binding of messages that are in now.
        /// </summary>
        public IReadOnlyList<String>  VENMessageConnectionsIn
            => [ .. venMessageEndpoints.Keys ];

        /// <summary>
        /// How many notifications were told over connections of messages, and
        /// taken by the VEN.
        /// </summary>
        public Int64                  NotificationsToldOverMessages
            => Interlocked.Read(ref notificationsTold);

        /// <summary>
        /// How many notifications wait to be told over the given connection of
        /// messages: its VEN is not in, or is being told of them now.
        /// </summary>
        /// <param name="ConnectionId">The connection of this grid operator.</param>
        public Int32 NotificationsWaitingFor(String ConnectionId)

            => notificationsWaiting.TryGetValue(ConnectionId, out var waiting)
                   ? waiting.Count
                   : 0;

        #endregion


        #region (private) BuildVENMessages(Server)

        /// <summary>
        /// Serve the JSON messages of OpenADR on the given WebSocket server, and
        /// make what serves them over SSH; and tell the subscriptions made over
        /// them.
        /// </summary>
        private void BuildVENMessages(StreamWebSocketServer Server)
        {

            Server.OnMessageConnection       += connection => ServeMessagesOverWebSocket(Server, connection);
            Server.OnTextMessageOf            = (connection, text) => venMessageSessions.TryGetValue(connection, out var session)
                                                                          ? session.Endpoint.ReceiveAsync(text)
                                                                          : Task.CompletedTask;
            Server.OnMessageConnectionEnded  += connection => EndMessages(connection, "the WebSocket ended");

            venMessageServer = new SshMessageServer(new SshMessageServerOptions {
                                   Subsystem     = OpenADRWireNames.SshSubsystem,
                                   Subprotocols  = [ OpenADRWireNames.Subprotocol ]
                               });

            venMessageServer.OnNewConnection        += (timestamp, sender, connection, cancellationToken) => {
                                                           ServeMessagesOverSSHConnection(connection);
                                                           return Task.CompletedTask;
                                                       };

            venMessageServer.OnTextMessageReceived  += (timestamp, sender, connection, text, cancellationToken)
                                                           => venMessageSessions.TryGetValue(connection, out var session)
                                                                  ? session.Endpoint.ReceiveAsync(text)
                                                                  : Task.CompletedTask;

            venMessageServer.OnConnectionClosed     += (timestamp, sender, connection, close, cancellationToken) => {
                                                           EndMessages(connection, "the SSH channel ended");
                                                           return Task.CompletedTask;
                                                       };

            OnOpenADRNotification += TellOverMessages;

        }

        #endregion

        #region (private) ValidateVEN(Timestamp, Server, Connection, EventTrackingId, CancellationToken)

        /// <summary>
        /// The door of the WebSocket port of OpenADR: who signs in, as the
        /// connection of a VEN - and then in the binding that connection is
        /// written down with, by the subprotocol it asks for.
        /// </summary>
        private async Task<HTTPResponse?> ValidateVEN(DateTimeOffset             Timestamp,
                                                      AWebSocketEndpoint         Server,
                                                      WebSocketServerConnection  Connection,
                                                      EventTracking_Id           EventTrackingId,
                                                      CancellationToken          CancellationToken)
        {

            if (await VENSignIns.Validate(Timestamp, Server, Connection, EventTrackingId, CancellationToken) is HTTPResponse refused)
                return refused;

            if (Connection.TryGetCustomData(InboundConnectionKey) is String id &&
                Connections.Connections.FirstOrDefault(entry => entry.Id == id) is ConnectionEntry entry)
            {

                var spoken   = OpenADRWireNames.SubprotocolOf(entry.OpenADRBinding);
                var offered  = Connection.HTTPRequest?.SecWebSocketProtocol.ToArray() ?? [];

                if (!offered.Contains(spoken))
                {

                    Log.Warning($"'{entry.InboundName}' signed in from {Connection.RemoteSocket} asking for {(offered.Length == 0 ? "no subprotocol" : String.Join(", ", offered))}, " +
                                $"and its connection speaks {spoken}: it is not let in.",
                                "openadr", "ven", "auth");

                    // As the door of WWCP_Node has it: a connection without
                    // the request it came with is refused all the same.
                    if (Connection.HTTPRequest is null)
                        throw new InvalidOperationException(
                                  $"'{entry.InboundName}' reached the WebSocket port of OpenADR without an HTTP request; refusing it."
                              );

                    return new HTTPResponse.Builder(Connection.HTTPRequest) {
                               HTTPStatusCode  = HTTPStatusCode.BadRequest,
                               Server          = Server.HTTPServiceName,
                               Connection      = org.GraphDefined.Vanaheimr.Hermod.HTTP.ConnectionType.Close
                           }.AsImmutable;

                }

            }

            return null;

        }

        #endregion


        #region (private) ServeMessagesOverWebSocket(Server, Connection)

        /// <summary>
        /// A WebSocket of the JSON messages of OpenADR, let in by its door as
        /// the connection it came under.
        /// </summary>
        private void ServeMessagesOverWebSocket(StreamWebSocketServer      Server,
                                                WebSocketServerConnection  Connection)
        {

            if (Connection.TryGetCustomData(InboundConnectionKey) is not String id ||
                Connections.Connections.FirstOrDefault(entry => entry.Id == id) is not ConnectionEntry entry ||
                entry.InboundName is not String name)
            {
                // The door lets in nothing else; should it ever, nothing is served.
                Log.Warning($"A WebSocket of OpenADR from {Connection.RemoteSocket} came in as no local controller that connects here, and is closed.", "openadr", "ven", "auth");
                _ = Connection.Close(WebSocketFrame.ClosingStatusCode.PolicyViolation, "Not let in.");
                return;
            }

            StartMessages(Connection,
                          entry,
                          User_Id.Parse(name),
                          Connection.RemoteSocket,
                          venServerTLS ? "WebSocket TLS" : "WebSocket",
                          async (text, cancellationToken) => await Server.SendTextMessage(Connection, text, CancellationToken: cancellationToken) == SentStatus.Success,
                          because => Connection.Close(WebSocketFrame.ClosingStatusCode.PolicyViolation, because));

        }

        #endregion

        #region (private) ServeMessagesOverSSH(Context, CancellationToken)

        /// <summary>
        /// One session of the subsystem of the JSON messages of OpenADR: let in
        /// where its login is written down as a VEN that connects over SSH in the
        /// binding of messages, and served until it ends.
        /// </summary>
        private async ValueTask ServeMessagesOverSSH(SshSubsystemContext  Context,
                                                     CancellationToken    CancellationToken)
        {

            var login  = Context.Session.Username;
            var from   = Context.Session.Peer?.ToString() ?? "somewhere";

            if (InboundConnectionOf(ProtocolVersion.OpenADR(OpenADRBinding.Messages), login.ToString()) is not ConnectionEntry entry)
            {
                Log.Warning($"'{login}' asked for the JSON messages of OpenADR over SSH from {from}, and is no local controller that connects here in that binding.",
                            "openadr", "ven", "ssh", "auth");
                return;
            }

            if (!entry.IsSSH)
            {
                Log.Warning($"'{login}' connects to this {NodeNoun} over a WebSocket, and came over SSH from {from}.", "openadr", "ven", "ssh", "auth");
                return;
            }

            if (!entry.AutoConnect)
            {
                Log.Warning($"'{login}' is the name '{entry.Description.FirstText()}' comes under, a connection that is switched off.", "openadr", "ven", "ssh", "auth");
                return;
            }

            if (venMessageServer is null)
                return;

            using var both = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, vensStopping.Token);

            await venMessageServer.ServeAsync(Context, both.Token);

        }

        #endregion

        #region (private) ServeMessagesOverSSHConnection(Connection)

        /// <summary>
        /// The connection of messages of a session of the subsystem, once it said
        /// HELLO and was welcomed.
        /// </summary>
        private void ServeMessagesOverSSHConnection(SshMessageConnection Connection)
        {

            if (InboundConnectionOf(ProtocolVersion.OpenADR(OpenADRBinding.Messages), Connection.Username.ToString()) is not ConnectionEntry entry)
            {
                _ = Connection.Close(org.GraphDefined.Vanaheimr.Hermod.Messaging.MessageCloseCode.PolicyViolation, "Not let in.");
                return;
            }

            StartMessages(Connection,
                          entry,
                          Connection.Username,
                          Connection.RemoteSocket,
                          "SSH",
                          async (text, cancellationToken) => await Connection.SendTextMessage(text, cancellationToken) == SentStatus.Success,
                          because => Connection.Close(org.GraphDefined.Vanaheimr.Hermod.Messaging.MessageCloseCode.PolicyViolation, because));

        }

        #endregion

        #region (private) StartMessages(Key, Entry, Login, RemoteSocket, Transport, Send, Close) / EndMessages(Key, Because)

        /// <summary>
        /// Serve the JSON messages of OpenADR over what came in as the given
        /// connection, as the VEN it names - and tell it what waited for it, once
        /// it asked something over it.
        /// </summary>
        /// <remarks>
        /// A VEN may not be ready to be told the moment its connection is up: a
        /// Notify that came before it could take one would be lost. What it asks
        /// says it is ready - and a local controller asks the VTN at once over a
        /// connection that came up.
        /// </remarks>
        private void StartMessages(Object                                          Key,
                                   ConnectionEntry                                 Entry,
                                   User_Id                                         Login,
                                   IPSocket                                        RemoteSocket,
                                   String                                          Transport,
                                   Func<String, CancellationToken, Task<Boolean>>  Send,
                                   Func<String, Task>                              Close)
        {

            OpenADRMessageEndpoint? endpoint = null;

            endpoint = new OpenADRMessageEndpoint(
                           $"The VTN of this {NodeNoun}, to '{Login}'",
                           Send,
                           (action, payload, cancellationToken) => {

                               // The first it asks: ready to be told what waits.
                               if (endpoint is not null && endpointsThatAsked.TryAdd(endpoint, 0))
                                   TellWhatWaits(Entry.Id);

                               return AskTheVTN(Entry, Login, Transport, RemoteSocket, action, payload, cancellationToken);

                           }
                       ) {
                           OnProblem = problem => Log.Warning(problem, "openadr", "ven", "messages")
                       };

            venMessageSessions [Key]       = new VENMessageSession(Entry, VENDoor.Of(Entry), Login, Transport, endpoint, Close);
            venMessageEndpoints[Entry.Id]  = endpoint;

            VENCameIn(Entry, Login, RemoteSocket, Transport, "speaking the JSON messages of OpenADR");

            if (NotificationsWaitingFor(Entry.Id) is Int32 waiting and > 0)
                Log.Info($"'{Login}' is told of {waiting} notification{(waiting == 1 ? "" : "s")} that waited for it, once it asks something.", "openadr", "subscription", "messages");

        }

        /// <summary>
        /// What came in over the given connection of messages went.
        /// </summary>
        private void EndMessages(Object  Key,
                                 String  Because)
        {

            if (!venMessageSessions.TryRemove(Key, out var session))
                return;

            session.Endpoint.End(Because);

            endpointsThatAsked.TryRemove(session.Endpoint, out _);

            venMessageEndpoints.TryRemove(KeyValuePair.Create(session.Entry.Id, session.Endpoint));

            VENWentOut(session.Entry, session.Login, session.Transport);

        }

        #endregion


        #region (private) LetGoOfMessagesNoLongerLetIn()

        /// <summary>
        /// Let go of every connection of messages in now whose connection is no
        /// longer what let it in; and of what waits to be told over a connection
        /// that was removed or switched off.
        /// </summary>
        private void LetGoOfMessagesNoLongerLetIn()
        {

            foreach (var (key, session) in venMessageSessions.ToArray())
                if (WhyNoLongerLetIn(session.Door) is String why)
                {

                    Log.Info($"'{session.Login}' is let go of: {why}.", "openadr", "ven", "connections");

                    EndMessages(key, why);

                    _ = Task.Run(async () => {
                            try { await session.Close($"Let go of: {why}."); } catch { }
                        });

                }

            foreach (var connectionId in notificationsWaiting.Keys.ToArray())
                if (Connections.Connections.FirstOrDefault(entry => entry.Id == connectionId) is not { AutoConnect: true } &&
                    notificationsWaiting.TryRemove(connectionId, out var dropped) &&
                    dropped.Count > 0)
                {
                    Log.Info($"{dropped.Count} notification{(dropped.Count == 1 ? "" : "s")} for the connection '{connectionId}' are let go of: it was removed or switched off.",
                             "openadr", "subscription", "messages");
                }

        }

        #endregion


        #region (private) AskTheVTN(Entry, Login, Transport, RemoteSocket, Action, Payload, CancellationToken)

        /// <summary>
        /// A request of a VEN in the binding of messages, as the request of the
        /// REST API it stands for, put to the VTN of the tunnels as the VEN its
        /// connection names - and what the VTN answered, as the answer to it.
        /// </summary>
        private async Task<OpenADRCallOutcome> AskTheVTN(ConnectionEntry    Entry,
                                                         User_Id            Login,
                                                         String             Transport,
                                                         IPSocket           RemoteSocket,
                                                         String             Action,
                                                         JObject            Payload,
                                                         CancellationToken  CancellationToken)
        {

            if (OpenADRActions.RouteOf(Action) is not OpenADRActionRoute route)
                return OpenADRCallOutcome.Fail(OpenADRMessageErrors.NotImplemented,
                                               Action == OpenADRActions.Notify
                                                   ? $"'{Action}' is the VTN's to send, not a VEN's."
                                                   : $"'{Action}' is no action the VTN of this {NodeNoun} takes.");

            if (!OpenADRActions.TryToHTTPRequest(route, Payload, out var method, out var pathAndQuery, out var body, out var error))
                return OpenADRCallOutcome.Fail(OpenADRMessageErrors.FormationViolation, error, new JObject(new JProperty("status", 400)));

            var request = new StringBuilder().
                              Append($"{method} {OpenADRWireNames.RestTunnelPath}{pathAndQuery} HTTP/1.1\r\n").
                              Append($"Host: {MessagesHost}\r\n").
                              Append("Accept: application/json\r\n").
                              Append("Connection: close\r\n");

            var bodyBytes = body is null ? [] : Encoding.UTF8.GetBytes(body);

            if (body is not null)
                request.Append("Content-Type: application/json; charset=utf-8\r\n");

            if (body is not null || method is "POST" or "PUT")
                request.Append($"Content-Length: {bodyBytes.Length}\r\n");

            request.Append("\r\n");

            var oneRequest = new OneRequestStream([ .. Encoding.ASCII.GetBytes(request.ToString()), .. bodyBytes ]);

            await using (var tunnel = new VENTunnelStream(oneRequest, Entry.VEN ?? "", Entry.Id, Login.ToString(), Transport, ViaMessages: true))
            {
                await tunnelServer!.HandleHTTPStreamAsync(
                          tunnel,
                          IPSocket.Zero,
                          RemoteSocket,
                          CancellationToken: CancellationToken
                      );
            }

            if (!OneRequestStream.TryReadResponse(oneRequest.Response, out var status, out var responseBody))
                return OpenADRCallOutcome.Fail(OpenADRMessageErrors.InternalError,
                                               $"The VTN gave no answer to '{Action}' that could be read.",
                                               new JObject(new JProperty("status", 500)));

            return OpenADRActions.FromHTTPResponse(route, status, responseBody);

        }

        #endregion

        #region (private) TellOverMessages(Notification)

        /// <summary>
        /// Tell a VEN of what a subscription of it made over a connection of
        /// messages asked to be told of: by Notify over that connection - at
        /// once where it is in, or once it is in again, where it is not; one
        /// after the other, in the order they came.
        /// </summary>
        /// <remarks>
        /// What waits is kept in memory, for a while and up to a number: see
        /// <see cref="NotificationsKeptFor"/> and <see cref="NotificationsKeptPerConnection"/>.
        /// Nothing waits for a connection that is removed or switched off.
        /// </remarks>
        private void TellOverMessages(OpenADRNotification Notification)
        {

            if (!OpenADRSubscriptions.IsConnectionCallback(Notification.ObjectOperation.CallbackURL.ToString()) ||
                Notification.Subscription.Id?.ToString() is not String subscriptionId ||
                Subscriptions.ConnectionOf(subscriptionId) is not String connectionId)
            {
                return;
            }

            var what = $"{Notification.Notification.Operation} {Notification.Notification.ObjectType}";

            if (Connections.Connections.FirstOrDefault(entry => entry.Id == connectionId) is not { AutoConnect: true })
            {
                Log.Debug($"'{Notification.Subscription.ClientID}' is not told of {what}: its connection '{connectionId}' is removed or switched off.",
                          "openadr", "subscription", "messages");
                return;
            }

            var waiting = notificationsWaiting.GetOrAdd(connectionId, _ => new ConcurrentQueue<WaitingNotification>());

            waiting.Enqueue(new WaitingNotification(Notification, DateTimeOffset.UtcNow));

            while (waiting.Count > Math.Max(NotificationsKeptPerConnection, 1) && waiting.TryDequeue(out var oldest))
                Log.Warning($"'{oldest.Notification.Subscription.ClientID}' was not in for {NotificationsKeptPerConnection} notifications, and is not told of the oldest: " +
                            $"{oldest.Notification.Notification.Operation} {oldest.Notification.Notification.ObjectType} of {oldest.Since:u}.",
                            "openadr", "subscription", "messages");

            if (!venMessageEndpoints.ContainsKey(connectionId))
                Log.Debug($"'{Notification.Subscription.ClientID}' is not in now, and is told of {what} once it is.",
                          "openadr", "subscription", "messages");

            TellWhatWaits(connectionId);

        }

        #endregion

        #region (private) TellWhatWaits(ConnectionId) / StillToBeTold(ConnectionId, Waiting)

        /// <summary>
        /// Tell what waits for the given connection over it, one after the other,
        /// as long as its VEN is in and asked something over it - where nothing
        /// tells it already.
        /// </summary>
        private void TellWhatWaits(String ConnectionId)
        {

            if (!notificationsTelling.TryAdd(ConnectionId, 0))
                return;

            _ = Task.Run(async () => {

                    try
                    {

                        while (notificationsWaiting.TryGetValue(ConnectionId, out var waiting) &&
                               waiting.TryPeek(out var next) &&
                               venMessageEndpoints.TryGetValue(ConnectionId, out var endpoint) &&
                               !endpoint.HasEnded &&
                               endpointsThatAsked.ContainsKey(endpoint))
                        {

                            var notification  = next.Notification;
                            var what          = $"{notification.Notification.Operation} {notification.Notification.ObjectType}";

                            if (!StillToBeTold(ConnectionId, next))
                            {
                                waiting.TryDequeue(out _);
                                Log.Debug($"'{notification.Subscription.ClientID}' is no longer told of {what} of {next.Since:u}: it waited too long, or its subscription is gone.",
                                          "openadr", "subscription", "messages");
                                continue;
                            }

                            try
                            {

                                var outcome = await endpoint.AskAsync(OpenADRActions.Notify, notification.Notification.ToJSON());

                                waiting.TryDequeue(out _);

                                if (outcome.Succeeded)
                                    Interlocked.Increment(ref notificationsTold);

                                else
                                    Log.Warning($"'{notification.Subscription.ClientID}' answered the notification {what} with {outcome}.",
                                                "openadr", "subscription", "messages");

                            }
                            catch (Exception e) when (endpoint.HasEnded)
                            {
                                // Its connection went while it was told: told again once it is in.
                                Log.Debug($"'{notification.Subscription.ClientID}' went while it was told of {what}, and is told again once it is in: {e.Message}",
                                          "openadr", "subscription", "messages");
                                break;
                            }
                            catch (Exception e)
                            {
                                waiting.TryDequeue(out _);
                                Log.Warning($"'{notification.Subscription.ClientID}' could not be told of {what}: {e.Message}",
                                            "openadr", "subscription", "messages");
                            }

                        }

                    }
                    finally
                    {
                        notificationsTelling.TryRemove(ConnectionId, out _);
                    }

                    // What came while this was ending, for a VEN that is in and asked.
                    if (notificationsWaiting.TryGetValue(ConnectionId, out var left) && !left.IsEmpty &&
                        venMessageEndpoints.TryGetValue(ConnectionId, out var now)    && !now.HasEnded &&
                        endpointsThatAsked.ContainsKey(now))
                    {
                        TellWhatWaits(ConnectionId);
                    }

                });

        }

        /// <summary>
        /// Whether what waits is still to be told over the given connection: it
        /// has not waited too long, and its subscription is still one of that
        /// connection.
        /// </summary>
        private Boolean StillToBeTold(String               ConnectionId,
                                      WaitingNotification  Waiting)

            => DateTimeOffset.UtcNow - Waiting.Since <= NotificationsKeptFor &&
               Waiting.Notification.Subscription.Id?.ToString() is String subscriptionId &&
               Subscriptions.ConnectionOf(subscriptionId) == ConnectionId;

        #endregion

    }

}
