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
    /// While it is not in, what it would be told is lost: it reads what it
    /// missed when it asks next.
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
        /// its login, what it came over, and its end of the messages.
        /// </summary>
        private sealed record VENMessageSession(ConnectionEntry         Entry,
                                                User_Id                 Login,
                                                String                  Transport,
                                                OpenADRMessageEndpoint  Endpoint);

        private          SshMessageServer?                                       venMessageServer;

        // By what it came over - the WebSocket connection, the SSH connection
        // of messages - and by the connection of this grid operator it came
        // under, the latest of it.
        private readonly ConcurrentDictionary<Object, VENMessageSession>        venMessageSessions   = new (ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<String, OpenADRMessageEndpoint>   venMessageEndpoints  = new ();

        private          Int64                                                   notificationsTold;

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
                          async (text, cancellationToken) => await Server.SendTextMessage(Connection, text, CancellationToken: cancellationToken) == SentStatus.Success);

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
                          async (text, cancellationToken) => await Connection.SendTextMessage(text, cancellationToken) == SentStatus.Success);

        }

        #endregion

        #region (private) StartMessages(Key, Entry, Login, RemoteSocket, Transport, Send) / EndMessages(Key, Because)

        /// <summary>
        /// Serve the JSON messages of OpenADR over what came in as the given
        /// connection, as the VEN it names.
        /// </summary>
        private void StartMessages(Object                                          Key,
                                   ConnectionEntry                                 Entry,
                                   User_Id                                         Login,
                                   IPSocket                                        RemoteSocket,
                                   String                                          Transport,
                                   Func<String, CancellationToken, Task<Boolean>>  Send)
        {

            var endpoint = new OpenADRMessageEndpoint(
                               $"The VTN of this {NodeNoun}, to '{Login}'",
                               Send,
                               (action, payload, cancellationToken) => AskTheVTN(Entry, Login, Transport, RemoteSocket, action, payload, cancellationToken)
                           ) {
                               OnProblem = problem => Log.Warning(problem, "openadr", "ven", "messages")
                           };

            venMessageSessions [Key]       = new VENMessageSession(Entry, Login, Transport, endpoint);
            venMessageEndpoints[Entry.Id]  = endpoint;

            VENCameIn(Entry, Login, RemoteSocket, Transport, "speaking the JSON messages of OpenADR");

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

            venMessageEndpoints.TryRemove(KeyValuePair.Create(session.Entry.Id, session.Endpoint));

            VENWentOut(session.Entry, session.Login, session.Transport);

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
        /// messages asked to be told of: by Notify over that connection, where it
        /// is in.
        /// </summary>
        private void TellOverMessages(OpenADRNotification Notification)
        {

            if (!OpenADRSubscriptions.IsConnectionCallback(Notification.ObjectOperation.CallbackURL.ToString()) ||
                Notification.Subscription.Id?.ToString() is not String subscriptionId ||
                Subscriptions.ConnectionOf(subscriptionId) is not String connectionId)
            {
                return;
            }

            if (!venMessageEndpoints.TryGetValue(connectionId, out var endpoint))
            {
                Log.Debug($"'{Notification.Subscription.ClientID}' is not connected now, and is not told of {Notification.Notification.Operation} {Notification.Notification.ObjectType}.",
                          "openadr", "subscription", "messages");
                return;
            }

            _ = Task.Run(async () => {

                    try
                    {

                        var outcome = await endpoint.AskAsync(OpenADRActions.Notify, Notification.Notification.ToJSON());

                        if (outcome.Succeeded)
                            Interlocked.Increment(ref notificationsTold);

                        else
                            Log.Warning($"'{Notification.Subscription.ClientID}' answered the notification {Notification.Notification.Operation} {Notification.Notification.ObjectType} with {outcome}.",
                                        "openadr", "subscription", "messages");

                    }
                    catch (OpenADRMessageException e)
                    {
                        Log.Warning($"'{Notification.Subscription.ClientID}' could not be told of {Notification.Notification.Operation} {Notification.Notification.ObjectType}: {e.Message}",
                                    "openadr", "subscription", "messages");
                    }

                });

        }

        #endregion

    }

}
