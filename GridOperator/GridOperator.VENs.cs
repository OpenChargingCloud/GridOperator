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
using System.Net.Sockets;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.SSH;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.WebSocket;

using cloud.charging.open.protocols.WWCP.Node;
using cloud.charging.open.protocols.WWCP.Node.Logins;
using cloud.charging.open.protocols.WWCP.Node.OpenADR;
using cloud.charging.open.protocols.WWCP.Node.Streams;
using cloud.charging.open.protocols.WWCP.Node.Connections;
using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.SecureShell;

using cloud.charging.open.GridOperator.OpenADR;
using cloud.charging.open.GridOperator.Configuration;

using ConnectionType = cloud.charging.open.protocols.WWCP.Node.Connections.ConnectionType;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// The local controllers on this grid - its VENs - that connect over a
    /// WebSocket of OpenADR, on a port of its own, or over an SSH subsystem of
    /// OpenADR of the node's SSH server, rather than over HTTPS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What connects is written down as a connection</b> of the type "VEN",
    /// one the other end makes, in the store of connections beside the
    /// configuration file, naming the VEN it is taken as. Nothing that is not
    /// written down gets in, whatever it signs in with.
    /// </para>
    /// <para>
    /// <b>Who signs in is decided as at the WebSocket port of Modbus of a meter</b>,
    /// by the same checks (<see cref="WebSocketSignIns"/>) and the same kind of
    /// logins (<see cref="WebSocketLogins"/>): a password, a one-time password,
    /// a TLS client certificate of a chain this grid operator accepts or the key
    /// pin of one - and over SSH a password, a one-time password bound to the
    /// session or an SSH key. A login of a VEN over SSH gets the subsystem of
    /// OpenADR of its connection, and nothing else: no command line.
    /// </para>
    /// <para>
    /// <b>What it may do is what its VEN may do at the VTN</b>, decided by the
    /// VTN as over HTTPS: the limits meant for it, its own reports. In the REST
    /// binding the stream of the connection is HTTP/1.1 to a VTN of its own -
    /// the same code over the same store, on an HTTP server that listens
    /// nowhere and serves nothing else, so that the JSON API and the web
    /// interface of this grid operator are not reached through a tunnel. The
    /// VEN is the connection's - see <see cref="VENTunnelStream"/> -, and no
    /// token is asked for.
    /// </para>
    /// <para>
    /// <b>The JSON messages of OpenADR</b> - the binding of messages - go to the
    /// same VTN: each request is the request of the REST API it stands for, put
    /// to that VTN as over a tunnel - see GridOperator.VENMessages.cs. A
    /// connection speaks the binding it is written down with, and no other:
    /// over a WebSocket its subprotocol, over SSH its subsystem.
    /// </para>
    /// </remarks>
    public partial class GridOperator
    {

        #region Data

        /// <summary>
        /// The listener of the WebSocket port of OpenADR, as the node's
        /// certificate store names it: what a local controller connecting to it
        /// is shown.
        /// </summary>
        public const String VENListener = "openadr";

        /// <summary>
        /// What OpenADR over SSH is called in what the SSH server says.
        /// </summary>
        public const String OpenADROverSSHLabel = "OpenADR";

        /// <summary>
        /// What the port is to the log, where it can not be had.
        /// </summary>
        public static readonly NodePort              VENServerPort  = new ("The WebSocket server of OpenADR");

        /// <summary>
        /// What the door of the WebSocket port calls the port and who comes to it.
        /// </summary>
        public static readonly WebSocketSignInWords  VENWords       = new () {
                                                                          Port         = "the WebSocket port of OpenADR",
                                                                          Who          = "A local controller",
                                                                          PinnedLogin  = "a local controller",
                                                                          Realm        = "OpenADR",
                                                                          Tags         = [ "openadr", "ven" ],
                                                                          InboundTags  = [ "openadr", "ven" ]
                                                                      };

        private VENServerConfiguration                        venServerSettings   = new VENServerConfiguration().Effective();
        private StreamWebSocketServer?                        venServer;
        private LoginSSHSignIns?                              venSSHSignIns;

        /// <summary>
        /// The HTTP server of the tunnels: never listening, with nothing on it
        /// but the VTN.
        /// </summary>
        private HTTPServer?                                   tunnelServer;

        private volatile Boolean                              venServerStarted;
        private volatile Boolean                              venServerTLS;
        private volatile Boolean                              openADROverSSHServed;

        /// <summary>
        /// How many tunnels each connection of the store is in over now, by its id.
        /// </summary>
        private readonly ConcurrentDictionary<String, Int32>  vensIn              = new ();

        /// <summary>
        /// The logins in over a tunnel now, and how many tunnels each.
        /// </summary>
        private readonly ConcurrentDictionary<String, Int32>  loginsIn            = new ();

        private readonly CancellationTokenSource              vensStopping        = new ();

        #endregion

        #region Properties

        /// <summary>
        /// Which local controllers may sign in over a WebSocket or over SSH, and with what.
        /// </summary>
        public WebSocketLogins         WebSocketLogins         { get; private set; } = default!;

        /// <summary>
        /// Which chains the certificate of a local controller connecting over the
        /// WebSocket port of OpenADR may lead to.
        /// </summary>
        public ClientTrustStore        ClientTrust             { get; private set; } = default!;

        /// <summary>
        /// Who comes in at the WebSocket port of OpenADR: the sign-in checks of its door.
        /// </summary>
        public WebSocketSignIns        VENSignIns              { get; private set; } = default!;

        /// <summary>
        /// What the WebSocket port of OpenADR presents, out of the node's store:
        /// the listener "openadr".
        /// </summary>
        public ListenerCertificate     VENServerCertificate    { get; private set; } = default!;

        /// <summary>
        /// The VTN of the tunnels of the REST API: the VTN over HTTPS, once more,
        /// on an HTTP server of its own that listens nowhere.
        /// </summary>
        public OpenADRVTNAPI           TunnelVTN               { get; private set; } = default!;

        /// <summary>
        /// What the WebSocket server of OpenADR is configured as, with every
        /// field filled in.
        /// </summary>
        public VENServerConfiguration  VENServerSettings
            => venServerSettings;

        /// <summary>
        /// The WebSocket server of OpenADR, once it is built.
        /// </summary>
        public StreamWebSocketServer?  VENServer
            => venServer;

        /// <summary>
        /// Whether the WebSocket server of OpenADR is listening right now.
        /// </summary>
        public Boolean                 VENServerRunning
            => venServerStarted;

        /// <summary>
        /// Whether the WebSocket port of OpenADR speaks TLS, as decided when it started.
        /// </summary>
        public Boolean                 VENServerTLS
            => venServerTLS;

        /// <summary>
        /// Whether OpenADR is served over SSH right now.
        /// </summary>
        public Boolean                 OpenADROverSSHServed
            => openADROverSSHServed;

        /// <summary>
        /// The logins in over a WebSocket or over SSH now.
        /// </summary>
        public IReadOnlyList<String>   VENLoginsIn
            => [.. loginsIn.Where(login => login.Value > 0).Select(login => login.Key)];

        #endregion


        #region (private) BuildVENServer()

        /// <summary>
        /// The stores of who connects as a VEN over a WebSocket or over SSH, the
        /// WebSocket server, the subsystems over SSH and the VTN of the tunnels.
        /// Nothing listens yet.
        /// </summary>
        private void BuildVENServer()
        {

            var directory = Path.GetDirectoryName(Path.GetFullPath(ConfigFile.Path)) ?? ".";

            venServerSettings = (ReadTheVENServer() ?? new VENServerConfiguration()).Effective();

            #region The connections: VENs that connect, over OpenADR only

            Connections = new ConnectionStore(
                              Path.Combine(directory, ConnectionStore.DefaultDirectoryName),
                              TimeProvider,
                              NodeNoun
                          ) {

                              // A VEN, and nothing else: it connects, and this
                              // grid operator connects to nothing.
                              Rules = new ConnectionRules {
                                          Types                  = [ ConnectionType.VEN ],
                                          Outbound               = false,
                                          OCPP1_6                = false,
                                          NamesTheNodeItLeadsTo  = false
                                      }

                          };

            Connections.OnConnectionsChanged += SayWhichVENIsWaitedFor;
            Connections.OnNotice             += (level, message) => Log.Log(level, message, "openadr", "connections");

            // What every node that keeps connections has, though this one makes
            // none: the key its connections would sign in over SSH with.
            SshKey = new SshClientKey(
                         Path.Combine(Connections.Path, SshClientKey.DirectoryName),
                         $"gridoperator@{Environment.MachineName}",
                         NodeNoun:  NodeNoun
                     );

            SshKey.OnNotice += (level, message) => Log.Log(level, message, "openadr", "connections", "ssh", "security");

            #endregion

            #region What it presents, out of the node's store

            ListenerCertificates.KeepServingLast(VENListener);

            VENServerCertificate = ListenerCertificates.For(VENListener, "the WebSocket port of OpenADR");

            #endregion

            #region The two stores of who signs in

            ClientTrust      = new ClientTrustStore(
                                   Path.Combine(directory, ClientTrustStore.DefaultDirectoryName),
                                   TimeProvider,
                                   NodeNoun,
                                   "local controller"
                               );

            ClientTrust.OnNotice += (level, message) => Log.Log(level, message, "openadr", "tls", "trust");

            ClientTrust.KeepToItsOwner();

            WebSocketLogins  = new WebSocketLogins(
                                   Path.Combine(directory, WebSocketLogins.DefaultFileName),
                                   TimeProvider,
                                   NodeNoun
                               );

            WebSocketLogins.OnNotice += (level, message) => Log.Log(level, message, "openadr", "ven", "auth");

            if (!WebSocketLogins.TryLoad(out var loginsError))
                throw new InvalidOperationException($"{loginsError} Repair or remove it and start again.");

            WebSocketLogins.OnChanged += () => {

                if (venServer is not null)
                    VENSignIns.ApplyLogins(venServer);

                RecheckSSHSessions();

            };

            #endregion

            #region The door of the WebSocket port

            VENSignIns = new WebSocketSignIns(
                             Logins:               WebSocketLogins,
                             ClientTrust:          ClientTrust,
                             SignInBrake:          SignInBrake,
                             Log:                  Log,
                             Authentications:      () => venServerSettings.ClientAuthentication ?? VENServerConfiguration.DefaultClientAuthentication,
                             OverTLS:              () => venServerTLS,
                             ChecksRevocation:     () => venServerSettings.CheckCertificateRevocation == true,
                             SaysSignIns:          () => true,
                             // Of either binding: which one it speaks is said by
                             // the subprotocol it asks for, and checked against
                             // the connection by ValidateVEN.
                             InboundConnectionOf:  (server, name) => InboundConnectionOf(ProtocolVersion.OpenADR(OpenADRBinding.REST),     name) ??
                                                                     InboundConnectionOf(ProtocolVersion.OpenADR(OpenADRBinding.Messages), name),
                             LetInAsInbound:       LetInAsInbound,
                             LetInOthers:          (connection, login, how, detail, byCertificate) =>
                                                       $"'{login}' is no local controller that connects here: no connection is written down that comes under that name",
                             Words:                VENWords
                         );

            #endregion

            #region The server

            venServer = new StreamWebSocketServer(
                            venServerSettings.Address,
                            venServerSettings.TCPPort ?? VENServerConfiguration.DefaultTCPPort,
                            [ OpenADRWireNames.RestSubprotocol ],
                            $"OpenChargingCloud {Kind.Product} v{Version}",
                            I18NString.Create($"The VENs of this {NodeNoun}, over OpenADR"),
                            null,
                            VENSignIns.ValidateCertificate,
                            true,
                            venServerSettings.CheckCertificateRevocation,
                            MessageSubprotocols: [ OpenADRWireNames.Subprotocol ]
                        );

            // A UDP port is opened by nobody who did not ask for it.
            venServer.AcceptHTTP3 = false;

            venServer.OnValidateWebSocketConnection += ValidateVEN;
            VENSignIns.ApplyLogins(venServer);

            venServer.OnStreamConnection += (connection, stream) => _ = ServeOverWebSocket(connection, stream);

            BuildVENMessages(venServer);

            #endregion

            #region The VTN of the tunnels

            // An HTTP server that is never started: it parses what comes through
            // a tunnel, and has nothing on it but the VTN - so that a request
            // through a tunnel reaches the VTN, and neither the JSON API nor the
            // web interface. A request waits as long as a tunnel is open: a VEN
            // asks every half a minute, and its connection is not dropped for
            // having said nothing in between.
            tunnelServer  = new HTTPServer(
                                HTTPServerName:           $"OpenChargingCloud {Kind.Product} v{Version}",
                                Description:              I18NString.Create("The VTN of the tunnels of OpenADR"),
                                DisableMaintenanceTasks:  true,
                                DisableWardenTasks:       true,
                                AutoStart:                false,
                                HeaderReadTimeout:        TimeSpan.FromDays(1)
                            );

            TunnelVTN     = new OpenADRVTNAPI(
                                tunnelServer,
                                this,
                                HTTPPath.Parse(OpenADRWireNames.RestTunnelPath.TrimEnd('/'))
                            );

            #endregion

            #region OpenADR over SSH, on the node's SSH server

            venSSHSignIns = new LoginSSHSignIns(
                                WebSocketLogins,
                                OpenADRWireNames.RestSshSubsystem,
                                () => openADROverSSHServed,
                                TimeProvider,
                                () => SSHSubsystems.Select(subsystem => subsystem.Name),
                                SubsystemOf: login => Connections.Connections.FirstOrDefault(entry => entry.Inbound &&
                                                                                                      entry.Use         == ConnectionUse.OpenADR &&
                                                                                                      entry.InboundName == login.ToString()) is ConnectionEntry entry
                                                          ? OpenADRWireNames.SshSubsystemOf(entry.OpenADRBinding)
                                                          : OpenADRWireNames.RestSshSubsystem
                            );

            venSSHSignIns.OnNotice += (level, message) => Log.Log(level, message, "openadr", "ven", "auth", "ssh");

            AddSSHSignIns(venSSHSignIns);

            #endregion

            Log.Info(
                venServerSettings.IsEnabled
                    ? $"The WebSocket server of OpenADR will listen on {venServerSettings.Address}:{venServerSettings.TCPPort}, " +
                      $"speaking {OpenADRWireNames.Subprotocol} and {OpenADRWireNames.RestSubprotocol}" +
                      (venServerSettings.TakesSSH ? $", and over the SSH subsystems {OpenADRWireNames.SshSubsystem} and {OpenADRWireNames.RestSshSubsystem} as well." : ".")
                    : "The WebSocket server of OpenADR is switched off; no local controller connects over a WebSocket or over SSH.",
                "openadr", "ven"
            );

        }

        #endregion

        #region (private) ReadTheVENServer()

        /// <summary>
        /// The "webSocketServer" section of the configuration file, as it is when
        /// the grid operator starts.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the section says something this grid operator cannot do.</exception>
        private VENServerConfiguration? ReadTheVENServer()
        {

            if (!ConfigFile.TryLoadDocument(out var document, out _) ||
                document[VENServerConfiguration.SectionName] is not JToken section ||
                section.Type == JTokenType.Null)
                return null;

            if (section is not JObject json)
                throw new InvalidOperationException($"'{ConfigFile.Path}': '{VENServerConfiguration.SectionName}' must be an object: the WebSocket server of OpenADR.");

            if (!VENServerConfiguration.TryParse(json, out var configuration, out var error))
                throw new InvalidOperationException($"'{ConfigFile.Path}': {error}");

            return configuration;

        }

        #endregion

        #region (private) StartVENServer() / StopVENServer()

        /// <summary>
        /// Start listening for the local controllers, where it is meant to - and
        /// serve OpenADR over SSH, where the section says so.
        /// </summary>
        private async Task StartVENServer()
        {

            if (venServerStarted || !venServerSettings.IsEnabled || venServer is null)
            {
                SayWhichVENIsWaitedFor();
                return;
            }

            var wantsTLS = (venServerSettings.ClientAuthentication ?? VENServerConfiguration.DefaultClientAuthentication).Any(authentication => authentication.IsOverTLS());

            venServerTLS = wantsTLS && VENServerCertificate.Choose() is not null;

            venServer.ServerCertificateChainSelector = venServerTLS
                                                           ? (server, client) => VENServerCertificate.Current
                                                           : null;

            if (wantsTLS && !venServerTLS)
                Log.Warning(
                    "HTTP authentication over TLS or a TLS client certificate is configured, but there is no server certificate - so the " +
                    "WebSocket port of OpenADR is unencrypted and only HTTP authentication without TLS can be used on it. Make a TLS server " +
                    $"identity for the listener '{VENListener}'; the port encrypts from the next start.",
                    "openadr", "ven", "tls"
                );

            try
            {
                await venServer.Start();
            }
            catch (SocketException problem)
            {
                throw new PortUnavailableException(
                          venServerSettings.TCPPort ?? VENServerConfiguration.DefaultTCPPort,
                          problem,
                          VENServerPort
                      );
            }

            venServerStarted = true;

            Log.Notice(
                $"The local controllers connect to {(venServerTLS ? "wss" : "ws")}://{venServerSettings.Address}:{venServerSettings.TCPPort}/<its name> " +
                $"({(venServerTLS ? "TLS" : "unencrypted")}, {WebSocketLogins.EnabledCount} login(s), {ClientTrust.EnabledCount} accepted chain(s)).",
                "openadr", "ven"
            );

            if (venServerSettings.TakesSSH)
                ServeOpenADROverSSH();

            SayWhichVENIsWaitedFor();

        }

        /// <summary>
        /// Stop listening for the local controllers: whoever is in is let go of.
        /// </summary>
        private async Task StopVENServer()
        {

            await vensStopping.CancelAsync();

            if (!venServerStarted)
                return;

            venServerStarted = false;

            StopServingOpenADROverSSH();

            if (venServer is not null)
                await venServer.Shutdown($"The {NodeNoun} is shutting down.");

            Log.Info("The WebSocket server of OpenADR has stopped listening.", "openadr", "ven");

        }

        #endregion

        #region (private) ServeOpenADROverSSH() / StopServingOpenADROverSSH()

        private void ServeOpenADROverSSH()
        {

            if (openADROverSSHServed)
                return;

            openADROverSSHServed = true;

            try
            {

                AddSSHSubsystem(new SSHSubsystem(
                                    OpenADRWireNames.RestSshSubsystem,
                                    OpenADROverSSHLabel,
                                    ServeOverSSH
                                ));

                AddSSHSubsystem(new SSHSubsystem(
                                    OpenADRWireNames.SshSubsystem,
                                    OpenADRMessagesOverSSHLabel,
                                    ServeMessagesOverSSH
                                ));

            }
            catch (Exception e)
            {
                openADROverSSHServed = false;
                Log.Critical($"OpenADR over SSH could not be served: {e.Message}", "openadr", "ven", "ssh");
                return;
            }

            if (!SSHEnabled)
                Log.Warning($"OpenADR over SSH is switched on, but the SSH server of this {NodeNoun} is off - so no local controller can connect over SSH.",
                            "openadr", "ven", "ssh");

            else
                Log.Notice($"The local controllers may connect over SSH as well: {SSHURL}, subsystems {OpenADRWireNames.SshSubsystem} and {OpenADRWireNames.RestSshSubsystem}, host key {SSHHostKey ?? "not known yet"}.",
                           "openadr", "ven", "ssh");

        }

        private void StopServingOpenADROverSSH()
        {

            if (!openADROverSSHServed)
                return;

            openADROverSSHServed = false;

            RemoveSSHSubsystem(OpenADRWireNames.RestSshSubsystem);
            RemoveSSHSubsystem(OpenADRWireNames.SshSubsystem);

        }

        #endregion


        #region (private) ServeOverSSH(Context, CancellationToken)

        /// <summary>
        /// One session of the subsystem of the REST API of OpenADR: let in where
        /// its login is written down as a VEN that connects over SSH, and served
        /// until it ends.
        /// </summary>
        private async ValueTask ServeOverSSH(SshSubsystemContext  Context,
                                             CancellationToken    CancellationToken)
        {

            var login  = Context.Session.Username;
            var peer   = Context.Session.Peer;
            var from   = peer?.ToString() ?? "somewhere";

            if (InboundConnectionOf(ProtocolVersion.OpenADR(OpenADRBinding.REST), login.ToString()) is not ConnectionEntry entry)
            {
                Log.Warning($"'{login}' asked for OpenADR over SSH from {from}, and is no local controller that connects here: no connection is written down that comes under that name.",
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

            using var both = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, vensStopping.Token);

            await Serve(entry,
                        login,
                        Context.Channel.AsStream(),
                        IPSocket.Zero,
                        peer ?? IPSocket.Zero,
                        "SSH",
                        both.Token);

        }

        #endregion

        #region (private) ServeOverWebSocket(Connection, Stream)

        /// <summary>
        /// One WebSocket of the REST API of OpenADR, let in by its door as the
        /// connection it came under: served until it ends.
        /// </summary>
        private async Task ServeOverWebSocket(WebSocketServerConnection  Connection,
                                              WebSocketMessageStream     Stream)
        {

            if (Connection.TryGetCustomData(InboundConnectionKey) is not String id ||
                Connections.Connections.FirstOrDefault(entry => entry.Id == id) is not ConnectionEntry entry ||
                entry.InboundName is not String name)
            {
                // The door lets in nothing else; should it ever, nothing is served.
                Log.Warning($"A WebSocket of OpenADR from {Connection.RemoteSocket} came in as no local controller that connects here, and is closed.", "openadr", "ven", "auth");
                Stream.Dispose();
                return;
            }

            await Serve(entry,
                        User_Id.Parse(name),
                        Stream,
                        Connection.LocalSocket,
                        Connection.RemoteSocket,
                        venServerTLS ? "WebSocket TLS" : "WebSocket",
                        vensStopping.Token);

        }

        #endregion

        #region (private) Serve(Entry, Login, Stream, LocalSocket, RemoteSocket, Transport, CancellationToken)

        /// <summary>
        /// Serve the REST API of the VTN over the given stream to what came in as
        /// the given connection, as the VEN that connection names.
        /// </summary>
        private async Task Serve(ConnectionEntry    Entry,
                                 User_Id            Login,
                                 Stream             Stream,
                                 IPSocket           LocalSocket,
                                 IPSocket           RemoteSocket,
                                 String             Transport,
                                 CancellationToken  CancellationToken)
        {

            var ven     = Entry.VEN ?? "";
            var tunnel  = new VENTunnelStream(Stream, ven, Entry.Id, Login.ToString(), Transport);

            VENCameIn(Entry, Login, RemoteSocket, Transport, "speaking the REST API of OpenADR in a tunnel");

            try
            {

                // One connection of HTTP/1.1, kept alive as long as the tunnel
                // is open. What the HTTP server ends - a connection it closes
                // after its answer, a request it could not read - is the end of
                // the tunnel, as of a TCP connection: the VEN connects again.
                await tunnelServer!.HandleHTTPStreamAsync(
                          tunnel,
                          LocalSocket,
                          RemoteSocket,
                          CancellationToken: CancellationToken
                      );

            }
            finally
            {

                try { await tunnel.DisposeAsync(); } catch { }

                VENWentOut(Entry, Login, Transport);

            }

        }

        #endregion

        #region (private) VENCameIn(Entry, Login, RemoteSocket, Transport, Speaking) / VENWentOut(Entry, Login, Transport)

        /// <summary>
        /// What came in as the given connection is in: counted, written down as
        /// connected, and said.
        /// </summary>
        private void VENCameIn(ConnectionEntry  Entry,
                               User_Id          Login,
                               IPSocket         RemoteSocket,
                               String           Transport,
                               String           Speaking)
        {

            var ven = Entry.VEN ?? "";

            vensIn.  AddOrUpdate(Entry.Id,          1, (_, count) => count + 1);
            loginsIn.AddOrUpdate(Login.ToString(),  1, (_, count) => count + 1);

            Record(Entry, ConnectionStatus.Connected, $"'{Login}' is connected over {Transport}, as the VEN '{ven}', {Speaking}.");

            Log.Info($"'{Login}' connected from {RemoteSocket} over {Transport}, as the VEN '{ven}', {Speaking}" +
                     (OpenADR.Login(ven) is null ? $" - which is no local controller of this {NodeNoun}, so every request is refused." : "."),
                     "openadr", "ven", "connections");

        }

        /// <summary>
        /// What came in as the given connection went: no longer counted, and
        /// waited for again where nothing else of it is in.
        /// </summary>
        private void VENWentOut(ConnectionEntry  Entry,
                                User_Id          Login,
                                String           Transport)
        {

            loginsIn.AddOrUpdate(Login.ToString(), 0, (_, count) => Math.Max(count - 1, 0));
            var left = vensIn.AddOrUpdate(Entry.Id, 0, (_, count) => Math.Max(count - 1, 0));

            Log.Info($"'{Login}' is no longer connected over {Transport}.", "openadr", "ven", "connections");

            if (left == 0)
            {
                vensIn.TryRemove(KeyValuePair.Create(Entry.Id, 0));
                SayWhichVENIsWaitedFor();
            }

        }

        #endregion

        #region (private) SayWhichVENIsWaitedFor()

        /// <summary>
        /// Say of every connection of a VEN that is not in now that it is waited
        /// for - or why nothing can come.
        /// </summary>
        private void SayWhichVENIsWaitedFor()
        {

            if (Connections is null)
                return;

            foreach (var entry in Connections.Connections.Where(entry => entry.Inbound).ToArray())
            {

                if (vensIn.ContainsKey(entry.Id))
                    continue;

                var name  = entry.InboundName;

                var what  = !entry.AutoConnect
                                ? $"'{name}' is not let in: this connection is switched off."
                                : entry.IsSSH
                                      ? openADROverSSHServed && SSHEnabled
                                            ? $"Waiting for '{name}' to connect over SSH."
                                            : $"'{name}' can not connect: this {NodeNoun} serves no OpenADR over SSH."
                                      : venServerStarted
                                            ? $"Waiting for '{name}' to connect."
                                            : $"'{name}' can not connect: the WebSocket server of OpenADR of this {NodeNoun} is not listening.";

                if (ConnectionStates.TryGetValue(entry.Id, out var state) && state.Status == ConnectionStatus.Waiting && state.Said == what)
                    continue;

                Record(entry, ConnectionStatus.Waiting, what);

            }

        }

        #endregion

    }

}
