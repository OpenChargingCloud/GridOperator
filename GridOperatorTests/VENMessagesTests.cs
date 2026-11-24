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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.SSH;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.SSH.Client;

using cloud.charging.open.protocols.OpenADRv3.Messaging;
using cloud.charging.open.protocols.WWCP.Node.OpenADR;
using cloud.charging.open.protocols.WWCP.Node.Connections;
using cloud.charging.open.protocols.WWCP.Node.Streams;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The local controllers that connect to the grid operator in the binding
    /// of the JSON messages of OpenADR, over a WebSocket and over SSH: every
    /// action is the VTN's answer, as over HTTPS; a VEN sees what it may see;
    /// a subscription made over the connection is told over it; and nothing
    /// comes in in a binding or a transport it is not written down with.
    /// </summary>
    public class VENMessagesTests : AGridOperatorTests
    {

        #region Data

        private IPPort  venPort;

        #endregion

        #region (private sealed class) AVEN

        /// <summary>
        /// A VEN connected in the binding of messages: its end of the messages,
        /// what it was told, and what it goes over.
        /// </summary>
        private sealed class AVEN(OpenADRMessageEndpoint   Endpoint,
                                  ConcurrentQueue<JObject>  Notified,
                                  Func<Task>                Close,
                                  Func<Boolean>             IsGone) : IAsyncDisposable
        {

            public OpenADRMessageEndpoint    Endpoint  { get; } = Endpoint;
            public ConcurrentQueue<JObject>  Notified  { get; } = Notified;

            /// <summary>
            /// Whether what it goes over ended.
            /// </summary>
            public Boolean                   Gone      => IsGone();

            public Task<OpenADRCallOutcome> Ask(String Action, JObject? Payload = null)
                => Endpoint.AskAsync(Action, Payload ?? [], TimeSpan.FromSeconds(10));

            public async ValueTask DisposeAsync()
            {
                Endpoint.End("the test is over");
                try { await Close(); } catch { }
            }

        }

        #endregion


        #region (private) AStartedGrid()

        /// <summary>
        /// A grid operator, started, with its WebSocket port of OpenADR and its
        /// SSH server on ports of their own, OpenADR over SSH switched on, two
        /// grid connection points and a local controller behind each.
        /// </summary>
        private async Task<GridOperator> AStartedGrid()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => {

                           venPort = TestPorts.Free();

                           return GridOperatorFrom(
                                      new JObject(
                                          new JProperty("nts",             new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))))),
                                          new JProperty("gridOperator",    new JObject(new JProperty("@id", "DE*SWJ"), new JProperty("name", "Netze Beispielstadt"))),
                                          new JProperty("webSocketServer", new JObject(
                                              new JProperty("enabled",               true),
                                              new JProperty("address",               "127.0.0.1"),
                                              new JProperty("port",                  venPort.ToUInt16()),
                                              new JProperty("clientAuthentication",  new JArray("httpAuthWithoutTLS")),
                                              new JProperty("ssh",                   new JObject(new JProperty("enabled", true)))
                                          )),
                                          new JProperty("ssh",             new JObject(
                                              new JProperty("enabled",  true),
                                              new JProperty("port",     TestPorts.Free().ToUInt16())
                                          ))
                                      ).ToString()
                                  );

                       });

            Assert.That(grid.VENServerRunning,     Is.True, "The WebSocket server of OpenADR is switched on, and not running.");
            Assert.That(grid.OpenADROverSSHServed, Is.True, "OpenADR over SSH is switched on, and not served.");

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-2"), "test", out _, out error,     out _), Is.True, error);

            Assert.That(grid.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out _, out error, out _), Is.True, error);
            Assert.That(grid.TryAddLocalController(ALocalController("lc-nord",     "DE*GEF*P2", "GCP-2"), "test", out _, out _, out error, out _), Is.True, error);

            return grid;

        }

        #endregion

        #region (private) URLOf(Grid, Transport, Login)

        private String URLOf(GridOperator  Grid,
                             String        Transport,
                             String        Login)

            => Transport == "ssh"
                   ? $"ssh://{Login}@127.0.0.1:{Grid.SSHPort!.Value.ToUInt16()}"
                   : $"ws://127.0.0.1:{venPort.ToUInt16()}/{Login}";

        #endregion

        #region (private) AVENThatConnects(Grid, Transport, Login, VEN, Binding = "messages", Enabled = true)

        /// <summary>
        /// A connection of a VEN that comes under the given login, over the
        /// given transport, and the password it signs in with.
        /// </summary>
        private String AVENThatConnects(GridOperator  Grid,
                                        String        Transport,
                                        String        Login,
                                        String        VEN,
                                        String        Binding  = "messages",
                                        Boolean       Enabled  = true)
        {

            var password = "a-password-" + Guid.NewGuid().ToString("N")[..12];

            Assert.That(Grid.Connections.TryAddConnection(I18NString.Create($"The local controller of '{VEN}'"),
                                                          URL.Parse(URLOf(Grid, Transport, Login)),
                                                          "VEN",
                                                          Enabled,
                                                          null,
                                                          null,
                                                          out _,
                                                          out var error,
                                                          Inbound:         true,
                                                          OpenADRBinding:  Binding,
                                                          VEN:             VEN),
                        Is.True, error);

            Assert.That(Grid.WebSocketLogins.TrySetPasswordAuthentication(User_Id.Parse(Login), password, null, I18NString.Create(VEN), out error), Is.True, error);

            return password;

        }

        #endregion

        #region (private) Connect(Grid, Transport, Login, Password, Subprotocol = null, Subsystem = null)

        /// <summary>
        /// A VEN connected in the binding of messages over the given transport,
        /// signed in with the given password - or null where it was not let in.
        /// </summary>
        private async Task<AVEN?> Connect(GridOperator  Grid,
                                          String        Transport,
                                          String        Login,
                                          String        Password,
                                          String?       Subprotocol  = null,
                                          String?       Subsystem    = null)
        {

            var notified = new ConcurrentQueue<JObject>();

            Task<OpenADRCallOutcome> Answer(String Action, JObject Payload, CancellationToken CancellationToken)
            {

                if (Action != OpenADRActions.Notify)
                    return Task.FromResult(OpenADRCallOutcome.Fail(OpenADRMessageErrors.NotImplemented, "Notify only."));

                notified.Enqueue(Payload);
                return Task.FromResult(OpenADRCallOutcome.Ok([]));

            }

            if (Transport == "ssh")
            {

                SSHClient ssh;

                try
                {
                    ssh = await SSHClient.Connect("127.0.0.1",
                                                  Grid.SSHPort!.Value,
                                                  new SSHClientOptions {
                                                      Username       = User_Id.Parse(Login),
                                                      Password       = Password,
                                                      VerifyHostKey  = hostKey => true
                                                  });
                }
                catch
                {
                    return null;
                }

                // Its end of the messages before it connects: what the grid
                // operator tells it at once - what waited for it - is not lost.
                SshMessageConnection? messages = null;

                var sshEndpoint = new OpenADRMessageEndpoint("a VEN over SSH",
                                                             async (text, cancellationToken) => messages is not null &&
                                                                                                await messages.SendTextMessage(text, cancellationToken) == org.GraphDefined.Vanaheimr.Hermod.WebSocket.SentStatus.Success,
                                                             Answer);

                var client = new SshMessageClient(new SshMessageClientOptions {
                                 Host          = "127.0.0.1",
                                 Connection    = _ => ValueTask.FromResult(ssh),
                                 Subsystem     = Subsystem ?? OpenADRWireNames.SshSubsystem,
                                 Subprotocols  = [ Subprotocol ?? OpenADRWireNames.Subprotocol ]
                             });

                client.OnTextMessageReceived += (timestamp, sender, connection, text, cancellationToken) => {
                                                    messages ??= connection;
                                                    return sshEndpoint.ReceiveAsync(text);
                                                };

                try
                {

                    messages = await client.ConnectAsync();

                    return new AVEN(sshEndpoint,
                                    notified,
                                    async () => {
                                        await client.Close();
                                        await ssh.DisposeAsync();
                                    },
                                    () => !client.IsConnected);

                }
                catch
                {
                    try { await client.DisposeAsync(); } catch { }
                    await ssh.DisposeAsync();
                    return null;
                }

            }

            // Its end of the messages before it connects: what the grid operator
            // tells it at once - what waited for it - is not lost.
            OpenADRMessageEndpoint? endpoint = null;

            var webSocket = new MessageWebSocketClient(
                                URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/{Login}"),
                                Subprotocol ?? OpenADRWireNames.Subprotocol,
                                HTTPBasicAuthentication.Create(Login, Password),
                                null,
                                null,
                                null,
                                null,
                                null,
                                null
                            ) {
                                OnText = text => endpoint?.ReceiveAsync(text) ?? Task.CompletedTask
                            };

            endpoint = new OpenADRMessageEndpoint("a VEN over a WebSocket", webSocket.SendText, Answer);

            var (_, response) = await webSocket.Connect();

            if (response.HTTPStatusCode != HTTPStatusCode.SwitchingProtocols)
            {
                webSocket.End();
                await webSocket.DisposeAsync();
                return null;
            }

            Assert.That(response.SecWebSocketProtocol, Is.EqualTo(Subprotocol ?? OpenADRWireNames.Subprotocol));

            return new AVEN(endpoint,
                            notified,
                            async () => {
                                webSocket.End();
                                await webSocket.DisposeAsync();
                            },
                            () => webSocket.Ended.IsCompleted);

        }

        #endregion

        #region (internal static) ChangeTheConnectionOf(Grid, Login, Change)

        /// <summary>
        /// Change the connection the given login comes under: "off" switches it
        /// off, "on" on again, "rest" and "messages" write it down for that
        /// binding, "removed" removes it.
        /// </summary>
        internal static void ChangeTheConnectionOf(GridOperator  Grid,
                                                   String        Login,
                                                   String        Change)
        {

            var entry  = Grid.Connections.Connections.First(entry => entry.InboundName == Login);

            String? error;

            var done   = Change == "removed"
                             ? Grid.Connections.TryRemoveConnection(entry.Id, out error)
                             : Grid.Connections.TryUpdateConnection(entry.Id,
                                                                    entry.Description,
                                                                    entry.URL,
                                                                    "VEN",
                                                                    Change != "off",
                                                                    null,
                                                                    null,
                                                                    out error,
                                                                    Inbound:         true,
                                                                    OpenADRBinding:  Change is "rest" or "messages"
                                                                                         ? Change
                                                                                         : entry.OpenADRBinding == OpenADRBinding.REST ? "rest" : "messages",
                                                                    VEN:             entry.VEN);

            Assert.That(done, Is.True, error);

        }

        #endregion

        #region (private) Subscribe(Grid, VEN, Transport, Login, Name)

        /// <summary>
        /// The given VEN subscribes to the events over its connection.
        /// </summary>
        private async Task Subscribe(GridOperator  Grid,
                                     AVEN          VEN,
                                     String        Transport,
                                     String        Login,
                                     String        Name)
        {

            var subscription  = JObject.Parse($$"""{ "clientName": "{{Name}}", "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE", "UPDATE", "DELETE" ], "callbackUrl": "{{URLOf(Grid, Transport, Login)}}" } ] }""");
            var subscribed    = await VEN.Ask(OpenADRActions.CreateSubscription, new JObject(new JProperty("subscription", subscription)));

            Assert.That(subscribed.Succeeded, Is.True, subscribed.ToString());

        }

        #endregion

        #region (private) ALimitFor(Grid, KW)

        /// <summary>
        /// A limit of the given kW sent to the point lc-paradies is behind.
        /// </summary>
        private static String ALimitFor(GridOperator  Grid,
                                        Int32         KW)
        {

            Assert.That(Grid.TrySendLimit(JObject.Parse($$"""{ "limitKW": {{KW}}, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var limit, out var error, out _), Is.True, error);

            return limit!.Id.ToString();

        }

        #endregion

        #region (private static) WaitFor(Condition, Seconds = 10)

        private static async Task<Boolean> WaitFor(Func<Boolean> Condition, Double Seconds = 10)
        {

            var until = DateTimeOffset.UtcNow.AddSeconds(Seconds);

            while (DateTimeOffset.UtcNow < until)
            {
                if (Condition())
                    return true;
                await Task.Delay(50);
            }

            return Condition();

        }

        #endregion


        #region EveryAction_IsTheAnswerOfTheVTN(Transport)

        /// <summary>
        /// Every action a VEN asks in the binding of messages is answered as
        /// the REST API answers it: the lists, the objects, a report written,
        /// changed and removed, a subscription made, changed and removed, the
        /// notifiers - as the VEN the connection names, without a token.
        /// </summary>
        [TestCase("ws")]
        [TestCase("ssh")]
        public async Task EveryAction_IsTheAnswerOfTheVTN(String Transport)
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, Transport, "lc-1", "lc-paradies");

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var limit, out var error, out _), Is.True, error);

            await using var ven = await Connect(grid, Transport, "lc-1", password);

            Assert.That(ven, Is.Not.Null, "The VEN was not let in.");
            Assert.That(await WaitFor(() => grid.VENMessageConnectionsIn.Count == 1), Is.True, "The grid operator does not see the VEN as in.");

            var programs       = await ven!.Ask(OpenADRActions.GetPrograms);
            var programId      = OpenADRActions.ListOf(OpenADRActions.GetPrograms, programs.Payload ?? [])[0]["id"]?.Value<String>();
            var program        = await ven.Ask(OpenADRActions.GetProgram,  new JObject(new JProperty("programID", programId)));

            var events         = await ven.Ask(OpenADRActions.GetEvents,   new JObject(new JProperty("active", true), new JProperty("targets", new JArray("ven:lc-paradies"))));
            var oneEvent       = await ven.Ask(OpenADRActions.GetEvent,    new JObject(new JProperty("eventID", limit!.Id.ToString())));

            var vens           = await ven.Ask(OpenADRActions.GetVENs);
            var venId          = OpenADRActions.ListOf(OpenADRActions.GetVENs, vens.Payload ?? [])[0]["id"]?.Value<String>();
            var oneVEN         = await ven.Ask(OpenADRActions.GetVEN,      new JObject(new JProperty("venID", venId)));
            var resources      = await ven.Ask(OpenADRActions.GetResources);

            var report         = JObject.Parse($$"""{ "eventID": "{{limit.Id}}", "clientName": "lc-paradies", "resources": [] }""");
            var created        = await ven.Ask(OpenADRActions.CreateReport, new JObject(new JProperty("report", report)));
            var reportId       = created.Payload?["id"]?.Value<String>();
            report["reportName"] = "changed";
            var updated        = await ven.Ask(OpenADRActions.UpdateReport, new JObject(new JProperty("reportID", reportId), new JProperty("report", report)));
            var reports        = await ven.Ask(OpenADRActions.GetReports);
            var oneReport      = await ven.Ask(OpenADRActions.GetReport,    new JObject(new JProperty("reportID", reportId)));
            var deleted        = await ven.Ask(OpenADRActions.DeleteReport, new JObject(new JProperty("reportID", reportId)));
            var reportGone     = await ven.Ask(OpenADRActions.GetReport,    new JObject(new JProperty("reportID", reportId)));

            var callback       = URLOf(grid, Transport, "lc-1");
            var subscription   = JObject.Parse($$"""{ "clientName": "lc-paradies", "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE", "DELETE" ], "callbackUrl": "{{callback}}" } ] }""");
            var subscribed     = await ven.Ask(OpenADRActions.CreateSubscription, new JObject(new JProperty("subscription", subscription)));
            var subscriptionId = subscribed.Payload?["id"]?.Value<String>();
            subscription["clientName"] = "renamed";
            var resubscribed   = await ven.Ask(OpenADRActions.UpdateSubscription, new JObject(new JProperty("subscriptionID", subscriptionId), new JProperty("subscription", subscription)));
            var subscriptions  = await ven.Ask(OpenADRActions.GetSubscriptions,   new JObject(new JProperty("clientName", "renamed")));
            var oneSubscr      = await ven.Ask(OpenADRActions.GetSubscription,    new JObject(new JProperty("subscriptionID", subscriptionId)));
            var unsubscribed   = await ven.Ask(OpenADRActions.DeleteSubscription, new JObject(new JProperty("subscriptionID", subscriptionId)));

            var notifiers      = await ven.Ask(OpenADRActions.GetNotifiers);

            Assert.Multiple(() => {

                Assert.That(programs.Succeeded,                                                              Is.True, programs.ToString());
                Assert.That(program.Payload?["id"]?.Value<String>(),                                         Is.EqualTo(programId));

                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetEvents, events.Payload ?? []),           Has.Count.EqualTo(1), events.ToString());
                Assert.That(oneEvent.Payload?["id"]?.Value<String>(),                                        Is.EqualTo(limit.Id.ToString()));

                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetVENs, vens.Payload ?? []),               Has.Count.EqualTo(1));
                Assert.That(oneVEN.Payload?["venName"]?.Value<String>(),                                     Is.EqualTo("lc-paradies"));
                Assert.That(resources.Succeeded,                                                             Is.True, resources.ToString());

                Assert.That(created.Succeeded,                                                               Is.True, created.ToString());
                Assert.That(created.Payload?["clientID"]?.Value<String>(),                                   Is.EqualTo("lc-paradies"), "the VTN writes the clientID of the VEN of the connection");
                Assert.That(updated.Payload?["reportName"]?.Value<String>(),                                 Is.EqualTo("changed"), updated.ToString());
                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetReports, reports.Payload ?? []),         Has.Count.EqualTo(1));
                Assert.That(oneReport.Payload?["id"]?.Value<String>(),                                       Is.EqualTo(reportId));
                Assert.That(deleted.Succeeded,                                                               Is.True, deleted.ToString());
                Assert.That(reportGone.ErrorCode,                                                            Is.EqualTo(OpenADRMessageErrors.NotFound));
                Assert.That(reportGone.Status,                                                               Is.EqualTo(404));

                Assert.That(subscribed.Succeeded,                                                            Is.True, subscribed.ToString());
                Assert.That(resubscribed.Payload?["clientName"]?.Value<String>(),                            Is.EqualTo("renamed"), resubscribed.ToString());
                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetSubscriptions, subscriptions.Payload ?? []), Has.Count.EqualTo(1));
                Assert.That(oneSubscr.Payload?["id"]?.Value<String>(),                                       Is.EqualTo(subscriptionId));
                Assert.That(unsubscribed.Succeeded,                                                          Is.True, unsubscribed.ToString());

                Assert.That(notifiers.Payload?["WEBHOOK"]?.Value<Boolean>(),                                 Is.True, notifiers.ToString());

            });

        }

        #endregion

        #region WhatIsWrong_IsAnsweredAsOCPPJAnswersIt()

        /// <summary>
        /// An action the VTN does not take, Notify asked of the VTN, a property
        /// an action does not take, and a message that is no frame are each
        /// answered with their error - and the connection stays.
        /// </summary>
        [Test]
        public async Task WhatIsWrong_IsAnsweredAsOCPPJAnswersIt()
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "ws", "lc-1", "lc-paradies");

            await using var ven = await Connect(grid, "ws", "lc-1", password);

            Assert.That(ven, Is.Not.Null, "The VEN was not let in.");

            var unknown    = await ven!.Ask("Reboot");
            var notify     = await ven.Ask(OpenADRActions.Notify, new JObject(new JProperty("objectType", "EVENT")));
            var misspelt   = await ven.Ask(OpenADRActions.GetEvents, new JObject(new JProperty("activ", true)));
            var noId       = await ven.Ask(OpenADRActions.GetEvent);
            var still      = await ven.Ask(OpenADRActions.GetNotifiers);

            Assert.Multiple(() => {
                Assert.That(unknown. ErrorCode,  Is.EqualTo(OpenADRMessageErrors.NotImplemented));
                Assert.That(notify.  ErrorCode,  Is.EqualTo(OpenADRMessageErrors.NotImplemented));
                Assert.That(misspelt.ErrorCode,  Is.EqualTo(OpenADRMessageErrors.FormationViolation));
                Assert.That(noId.    ErrorCode,  Is.EqualTo(OpenADRMessageErrors.FormationViolation));
                Assert.That(still.   Succeeded,  Is.True, "The connection did not stay.");
            });

        }

        #endregion

        #region AVEN_SeesOnlyWhatItMaySee()

        /// <summary>
        /// A limit sent to the local controller behind the other grid connection
        /// point is not listed for this VEN, nor read by its id; its reports
        /// are not the other's.
        /// </summary>
        [Test]
        public async Task AVEN_SeesOnlyWhatItMaySee()
        {

            var grid          = await AStartedGrid();
            var paradiesPass  = AVENThatConnects(grid, "ws",  "lc-1", "lc-paradies");
            var nordPass      = AVENThatConnects(grid, "ssh", "lc-2", "lc-nord");

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 11, "durationMinutes": 60, "gridConnectionPointId": "GCP-2" }"""), "test", out var nordLimit, out var error, out _), Is.True, error);

            await using var paradies  = await Connect(grid, "ws",  "lc-1", paradiesPass);
            await using var nord      = await Connect(grid, "ssh", "lc-2", nordPass);

            Assert.That(paradies, Is.Not.Null, "lc-paradies was not let in.");
            Assert.That(nord,     Is.Not.Null, "lc-nord was not let in.");

            var paradiesEvents  = await paradies!.Ask(OpenADRActions.GetEvents);
            var paradiesOne     = await paradies. Ask(OpenADRActions.GetEvent, new JObject(new JProperty("eventID", nordLimit!.Id.ToString())));
            var nordEvents      = await nord!.    Ask(OpenADRActions.GetEvents);

            var report          = JObject.Parse($$"""{ "eventID": "{{nordLimit.Id}}", "clientName": "lc-nord", "resources": [] }""");
            var nordReport      = await nord.Ask(OpenADRActions.CreateReport, new JObject(new JProperty("report", report)));
            var paradiesReports = await paradies.Ask(OpenADRActions.GetReports);
            var paradiesReport  = await paradies.Ask(OpenADRActions.GetReport, new JObject(new JProperty("reportID", nordReport.Payload?["id"]?.Value<String>())));

            Assert.Multiple(() => {
                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetEvents,  paradiesEvents. Payload ?? []),  Is.Empty, "lc-paradies listed the limit of lc-nord.");
                Assert.That(paradiesOne.Status,                                                                Is.EqualTo(404), "lc-paradies read the limit of lc-nord.");
                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetEvents,  nordEvents.     Payload ?? []),  Has.Count.EqualTo(1));
                Assert.That(nordReport.Succeeded,                                                              Is.True, nordReport.ToString());
                Assert.That(OpenADRActions.ListOf(OpenADRActions.GetReports, paradiesReports.Payload ?? []),  Is.Empty, "lc-paradies listed the report of lc-nord.");
                Assert.That(paradiesReport.Status,                                                             Is.EqualTo(404), "lc-paradies read the report of lc-nord.");
            });

        }

        #endregion

        #region ASubscription_IsToldOverItsConnection_AndOnlyToItsVEN(Transport)

        /// <summary>
        /// A VEN that subscribed to the events over its connection is told of a
        /// limit sent to it, and of the limit cancelled, by Notify over it; the
        /// other VEN, subscribed as well, is told of neither - nor of a limit it
        /// may not see; and a subscription with a callback of a connection is
        /// refused over HTTPS.
        /// </summary>
        [TestCase("ws")]
        [TestCase("ssh")]
        public async Task ASubscription_IsToldOverItsConnection_AndOnlyToItsVEN(String Transport)
        {

            var grid          = await AStartedGrid();
            var paradiesPass  = AVENThatConnects(grid, Transport, "lc-1", "lc-paradies");
            var nordPass      = AVENThatConnects(grid, Transport, "lc-2", "lc-nord");

            await using var paradies  = await Connect(grid, Transport, "lc-1", paradiesPass);
            await using var nord      = await Connect(grid, Transport, "lc-2", nordPass);

            Assert.That(paradies, Is.Not.Null, "lc-paradies was not let in.");
            Assert.That(nord,     Is.Not.Null, "lc-nord was not let in.");

            foreach (var (ven, login, name) in new[] { (paradies!, "lc-1", "lc-paradies"), (nord!, "lc-2", "lc-nord") })
            {
                var subscription = JObject.Parse($$"""{ "clientName": "{{name}}", "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE", "UPDATE", "DELETE" ], "callbackUrl": "{{URLOf(grid, Transport, login)}}" } ] }""");
                var subscribed   = await ven.Ask(OpenADRActions.CreateSubscription, new JObject(new JProperty("subscription", subscription)));
                Assert.That(subscribed.Succeeded, Is.True, subscribed.ToString());
            }

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var limit, out var error, out _), Is.True, error);

            Assert.That(await WaitFor(() => paradies!.Notified.Count >= 1), Is.True, "lc-paradies was not told of its limit.");

            Assert.That(grid.TryCancelLimit(limit!.Id.ToString(), "test", out error, out _), Is.True, error);

            Assert.That(await WaitFor(() => paradies!.Notified.Count >= 2), Is.True, "lc-paradies was not told of its limit cancelled.");

            // Time enough for anything sent to lc-nord to have come.
            await Task.Delay(500);

            var told = paradies!.Notified.ToArray();

            Assert.Multiple(() => {
                Assert.That(told[0]["operation"]?.Value<String>(),                Is.EqualTo("CREATE"));
                Assert.That(told[0]["objectType"]?.Value<String>(),               Is.EqualTo("EVENT"));
                Assert.That(told[0]["object"]?["id"]?.Value<String>(),            Is.EqualTo(limit.Id.ToString()));
                Assert.That(told[0]["object"]?["targets"]?.Values<String>(),      Is.EquivalentTo(new[] { "ven:lc-paradies" }), "target hiding");
                Assert.That(told[1]["operation"]?.Value<String>(),                Is.EqualTo("DELETE"));
                Assert.That(nord!.Notified,                                       Is.Empty, "lc-nord was told of the limit of lc-paradies.");
                Assert.That(grid.NotificationsToldOverMessages,                   Is.EqualTo(2));
            });

        }

        #endregion

        #region AConnectionCallback_IsNoCallback_OverHTTPS()

        /// <summary>
        /// Over the REST API in a tunnel, a callback of a connection is no
        /// callback: a subscription with it is refused.
        /// </summary>
        [Test]
        public async Task AConnectionCallback_IsNoCallback_OverHTTPS()
        {

            var grid = await AStartedGrid();

            Assert.That(grid.TryNewSecret("lc-paradies", "test", out _, out var secret, out var error, out _), Is.True, error);

            var token = grid.OpenADR.TryIssueToken("lc-paradies", secret!);

            using var client = new HttpClient { BaseAddress = new Uri(grid.OpenADRVTNURL) };
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token!.Value.Token);

            var subscription = $$"""{ "clientName": "x", "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE" ], "callbackUrl": "ws://127.0.0.1:{{venPort.ToUInt16()}}/lc-1" } ] }""";

            var response = await client.PostAsync("subscriptions", new StringContent(subscription, System.Text.Encoding.UTF8, "application/json"));

            Assert.That((Int32) response.StatusCode, Is.EqualTo(400));

        }

        #endregion

        #region WhatIsNotLetIn(Transport)

        /// <summary>
        /// A connection that is switched off, one of the REST binding asking for
        /// the messages, one of the messages asking for the REST binding, and a
        /// wrong password are not let in; the right one is.
        /// </summary>
        [TestCase("ws")]
        [TestCase("ssh")]
        public async Task WhatIsNotLetIn(String Transport)
        {

            var grid         = await AStartedGrid();

            var switchedOff  = AVENThatConnects(grid, Transport, "lc-off",   "lc-paradies", Enabled: false);
            var ofREST       = AVENThatConnects(grid, Transport, "lc-rest",  "lc-paradies", Binding: "rest");
            var right        = AVENThatConnects(grid, Transport, "lc-1",     "lc-paradies");

            var cases        = new List<(String Login, String Password, String? Subprotocol, String? Subsystem, String What)> {
                                   ("lc-off",  switchedOff,          null,                             null,                               "a connection that is switched off"),
                                   ("lc-rest", ofREST,               null,                             null,                               "a connection of the REST binding, asking for the messages"),
                                   ("lc-1",    "not-its-password",   null,                             null,                               "a wrong password")
                               };

            if (Transport == "ssh")
                cases.Add(("lc-1", right, OpenADRWireNames.RestSubprotocol, OpenADRWireNames.RestSshSubsystem, "a connection of the messages, asking for the subsystem of the REST binding"));
            else
                cases.Add(("lc-1", right, OpenADRWireNames.RestSubprotocol, null,                              "a connection of the messages, asking for the REST binding"));

            foreach (var (login, password, subprotocol, subsystem, what) in cases)
            {

                if (Transport == "ws" && subprotocol is not null)
                {
                    // Asking for the subprotocol of the REST binding is asking
                    // for a stream: the door says no before the upgrade.
                    var stream = new StreamWebSocketClient(URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/{login}"), subprotocol,
                                                           HTTPBasicAuthentication.Create(login, password), null, null, null, null, null, null);
                    var (_, response) = await stream.Connect();
                    await stream.DisposeAsync();
                    Assert.That(response.HTTPStatusCode, Is.Not.EqualTo(HTTPStatusCode.SwitchingProtocols), $"{what} was let in.");
                    continue;
                }

                await using var ven = await Connect(grid, Transport, login, password, subprotocol, subsystem);

                var answered = ven is not null &&
                               (await ven.Endpoint.AskAsync(OpenADRActions.GetNotifiers, [], TimeSpan.FromSeconds(2)).
                                                   ContinueWith(task => task.IsCompletedSuccessfully && task.Result.Succeeded));

                Assert.That(answered, Is.False, $"{what} was let in.");

            }

            await using var rightOne = await Connect(grid, Transport, "lc-1", right);

            Assert.That(rightOne, Is.Not.Null, "The connection written down was not let in.");
            Assert.That((await rightOne!.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True);

        }

        #endregion

        #region ASwitchedOffConnection_IsTurnedAwayForNow_AWrongPassword_ForGood()

        /// <summary>
        /// Over a WebSocket, a VEN that signs in rightly as a connection that is
        /// switched off is turned away for now - 503 with a Retry-After, so that
        /// it comes back by itself once its connection is on -, and one with a
        /// wrong password for good - 401.
        /// </summary>
        [Test]
        public async Task ASwitchedOffConnection_IsTurnedAwayForNow_AWrongPassword_ForGood()
        {

            var grid         = await AStartedGrid();

            var switchedOff  = AVENThatConnects(grid, "ws", "lc-off", "lc-paradies", Enabled: false);
            var right        = AVENThatConnects(grid, "ws", "lc-1",   "lc-nord");

            async Task<HTTPResponse> Answer(String Login, String Password)
            {

                var client = new StreamWebSocketClient(URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/{Login}"), OpenADRWireNames.Subprotocol,
                                                       HTTPBasicAuthentication.Create(Login, Password), null, null, null, null, null, null);

                var (_, response) = await client.Connect();

                await client.DisposeAsync();

                return response;

            }

            var off    = await Answer("lc-off", switchedOff);
            var wrong  = await Answer("lc-1",   "not-its-password");

            Assert.Multiple(() => {

                Assert.That(off.HTTPStatusCode,    Is.EqualTo(HTTPStatusCode.ServiceUnavailable), "A connection switched off was not turned away for now.");
                Assert.That(off.RetryAfter,        Is.Not.Null.And.Not.Empty,                      "A connection switched off was not told when to try again.");
                Assert.That(off.WWWAuthenticate,   Is.Null,                                        "A connection switched off was asked for other credentials.");

                Assert.That(wrong.HTTPStatusCode,  Is.EqualTo(HTTPStatusCode.Unauthorized),       "A wrong password was not turned away for good.");
                Assert.That(wrong.RetryAfter,      Is.Null.Or.Empty,                               "A wrong password was told to try again.");

            });

        }

        #endregion


        #region AConnectionNoLongerWhatLetItIn_IsLetGoOf(Transport, Change)

        /// <summary>
        /// A VEN in over its connection is let go of once the connection is no
        /// longer what let it in - switched off, written down for the REST API,
        /// removed -, and is not let in again; the other VEN stays in.
        /// </summary>
        [TestCase("ws",  "off")]
        [TestCase("ws",  "rest")]
        [TestCase("ws",  "removed")]
        [TestCase("ssh", "off")]
        [TestCase("ssh", "rest")]
        [TestCase("ssh", "removed")]
        public async Task AConnectionNoLongerWhatLetItIn_IsLetGoOf(String Transport, String Change)
        {

            var grid          = await AStartedGrid();
            var paradiesPass  = AVENThatConnects(grid, Transport, "lc-1", "lc-paradies");
            var nordPass      = AVENThatConnects(grid, Transport, "lc-2", "lc-nord");

            await using var paradies  = await Connect(grid, Transport, "lc-1", paradiesPass);
            await using var nord      = await Connect(grid, Transport, "lc-2", nordPass);

            Assert.That(paradies, Is.Not.Null, "lc-paradies was not let in.");
            Assert.That(nord,     Is.Not.Null, "lc-nord was not let in.");
            Assert.That((await paradies!.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True);

            ChangeTheConnectionOf(grid, "lc-1", Change);

            Assert.That(await WaitFor(() => paradies.Gone),                         Is.True,  $"lc-paradies was not let go of, its connection {Change}.");
            Assert.That(await WaitFor(() => grid.VENMessageConnectionsIn.Count == 1), Is.True, "The grid operator still counts lc-paradies in.");

            Assert.Multiple(() => {
                Assert.That(grid.VENLoginsIn,  Does.Not.Contain("lc-1"));
                Assert.That(nord!.Gone,        Is.False, "lc-nord was let go of as well.");
            });

            Assert.That((await nord!.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True, "lc-nord is no longer answered.");

            await using var again = await Connect(grid, Transport, "lc-1", paradiesPass);

            var answered = again is not null &&
                           (await again.Endpoint.AskAsync(OpenADRActions.GetNotifiers, [], TimeSpan.FromSeconds(2)).
                                             ContinueWith(task => task.IsCompletedSuccessfully && task.Result.Succeeded));

            Assert.That(answered, Is.False, $"lc-paradies was let in again, its connection {Change}.");

        }

        #endregion

        #region AConnectionSwitchedOn_LetsItsVENInAgain(Transport)

        /// <summary>
        /// A connection switched off and on again lets its VEN in again.
        /// </summary>
        [TestCase("ws")]
        [TestCase("ssh")]
        public async Task AConnectionSwitchedOn_LetsItsVENInAgain(String Transport)
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, Transport, "lc-1", "lc-paradies");

            await using var paradies = await Connect(grid, Transport, "lc-1", password);

            Assert.That(paradies, Is.Not.Null, "lc-paradies was not let in.");

            ChangeTheConnectionOf(grid, "lc-1", "off");

            Assert.That(await WaitFor(() => paradies!.Gone), Is.True, "lc-paradies was not let go of.");

            ChangeTheConnectionOf(grid, "lc-1", "on");

            await using var again = await Connect(grid, Transport, "lc-1", password);

            Assert.That(again, Is.Not.Null, "lc-paradies was not let in again.");
            Assert.That((await again!.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True);

        }

        #endregion

        #region WhatCameWhileItsVENWasOut_IsToldOnceItIsIn(Transport)

        /// <summary>
        /// A VEN subscribed over its connection, and out: what it is to be told
        /// waits, and is told in the order it came once it is in again and asked
        /// something - over its new connection, not before -, and nothing waits
        /// then.
        /// </summary>
        [TestCase("ws")]
        [TestCase("ssh")]
        public async Task WhatCameWhileItsVENWasOut_IsToldOnceItIsIn(String Transport)
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, Transport, "lc-1", "lc-paradies");
            var entry     = grid.Connections.Connections.First(entry => entry.InboundName == "lc-1").Id;

            var first     = await Connect(grid, Transport, "lc-1", password);

            Assert.That(first, Is.Not.Null, "lc-paradies was not let in.");

            await Subscribe(grid, first!, Transport, "lc-1", "lc-paradies");

            await first!.DisposeAsync();

            Assert.That(await WaitFor(() => grid.VENMessageConnectionsIn.Count == 0), Is.True, "lc-paradies is still counted in.");

            var limit = ALimitFor(grid, 44);

            Assert.That(grid.TryCancelLimit(limit, "test", out var error, out _), Is.True, error);

            Assert.That(await WaitFor(() => grid.NotificationsWaitingFor(entry) == 2), Is.True, $"Not both notifications wait: {grid.NotificationsWaitingFor(entry)}.");
            Assert.That(grid.NotificationsToldOverMessages,                            Is.Zero);

            await using var second = await Connect(grid, Transport, "lc-1", password);

            Assert.That(second, Is.Not.Null, "lc-paradies was not let in again.");

            // In, and not asked anything yet: not told yet.
            await Task.Delay(300);

            Assert.That(second!.Notified,                    Is.Empty,          "lc-paradies was told before it asked anything.");
            Assert.That(grid.NotificationsWaitingFor(entry),  Is.EqualTo(2));

            Assert.That((await second.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True);

            Assert.That(await WaitFor(() => second.Notified.Count == 2), Is.True, $"lc-paradies was told {second.Notified.Count} of 2 once it asked.");

            var told = second.Notified.ToArray();

            Assert.Multiple(() => {
                Assert.That(told[0]["operation"]?.Value<String>(),      Is.EqualTo("CREATE"), "not in the order they came");
                Assert.That(told[0]["object"]?["id"]?.Value<String>(),  Is.EqualTo(limit));
                Assert.That(told[1]["operation"]?.Value<String>(),      Is.EqualTo("DELETE"), "not in the order they came");
                Assert.That(grid.NotificationsWaitingFor(entry),        Is.Zero);
                Assert.That(grid.NotificationsToldOverMessages,         Is.EqualTo(2));
            });

        }

        #endregion

        #region WhatWaits_IsKeptUpToANumber_AndForAWhile()

        /// <summary>
        /// Of what waits, only the newest are kept, up to their number; and what
        /// waited too long is not told.
        /// </summary>
        [Test]
        public async Task WhatWaits_IsKeptUpToANumber_AndForAWhile()
        {

            var keptPerConnection  = GridOperator.NotificationsKeptPerConnection;
            var keptFor            = GridOperator.NotificationsKeptFor;

            try
            {

                GridOperator.NotificationsKeptPerConnection = 2;

                var grid      = await AStartedGrid();
                var password  = AVENThatConnects(grid, "ws", "lc-1", "lc-paradies");
                var entry     = grid.Connections.Connections.First(entry => entry.InboundName == "lc-1").Id;

                var first     = await Connect(grid, "ws", "lc-1", password);
                await Subscribe(grid, first!, "ws", "lc-1", "lc-paradies");
                await first!.DisposeAsync();

                Assert.That(await WaitFor(() => grid.VENMessageConnectionsIn.Count == 0), Is.True, "lc-paradies is still counted in.");

                var limits = new[] { ALimitFor(grid, 11), ALimitFor(grid, 22), ALimitFor(grid, 33) };

                Assert.That(await WaitFor(() => grid.NotificationsWaitingFor(entry) == 2), Is.True, $"{grid.NotificationsWaitingFor(entry)} wait, not 2.");

                await using (var second = await Connect(grid, "ws", "lc-1", password))
                {

                    Assert.That((await second!.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True);

                    Assert.That(await WaitFor(() => second.Notified.Count == 2), Is.True, $"lc-paradies was told {second.Notified.Count} of 2.");

                    Assert.That(second.Notified.Select(told => told["object"]?["id"]?.Value<String>()),
                                Is.EqualTo(limits[1..]),
                                "Not the newest two were kept.");

                }

                Assert.That(await WaitFor(() => grid.VENMessageConnectionsIn.Count == 0), Is.True, "lc-paradies is still counted in.");

                // And what waited too long.
                GridOperator.NotificationsKeptFor = TimeSpan.FromMilliseconds(200);

                ALimitFor(grid, 44);

                Assert.That(await WaitFor(() => grid.NotificationsWaitingFor(entry) == 1), Is.True, "The limit does not wait.");

                await Task.Delay(400);

                await using var third = await Connect(grid, "ws", "lc-1", password);

                Assert.That((await third!.Ask(OpenADRActions.GetNotifiers)).Succeeded, Is.True);

                Assert.That(await WaitFor(() => grid.NotificationsWaitingFor(entry) == 0), Is.True, "What waited too long still waits.");

                await Task.Delay(300);

                Assert.That(third!.Notified, Is.Empty, "lc-paradies was told of what waited too long.");

            }
            finally
            {
                GridOperator.NotificationsKeptPerConnection  = keptPerConnection;
                GridOperator.NotificationsKeptFor            = keptFor;
            }

        }

        #endregion

        #region WhatWaits_ForAConnectionSwitchedOff_IsLetGoOf()

        /// <summary>
        /// What waits for a connection that is then switched off is let go of,
        /// and nothing waits for it while it is off.
        /// </summary>
        [Test]
        public async Task WhatWaits_ForAConnectionSwitchedOff_IsLetGoOf()
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "ws", "lc-1", "lc-paradies");
            var entry     = grid.Connections.Connections.First(entry => entry.InboundName == "lc-1").Id;

            var first     = await Connect(grid, "ws", "lc-1", password);
            await Subscribe(grid, first!, "ws", "lc-1", "lc-paradies");
            await first!.DisposeAsync();

            Assert.That(await WaitFor(() => grid.VENMessageConnectionsIn.Count == 0), Is.True, "lc-paradies is still counted in.");

            ALimitFor(grid, 44);

            Assert.That(await WaitFor(() => grid.NotificationsWaitingFor(entry) == 1), Is.True, "The limit does not wait.");

            ChangeTheConnectionOf(grid, "lc-1", "off");

            Assert.That(await WaitFor(() => grid.NotificationsWaitingFor(entry) == 0), Is.True, "What waits for a connection switched off was kept.");

            ALimitFor(grid, 55);

            await Task.Delay(300);

            Assert.That(grid.NotificationsWaitingFor(entry), Is.Zero, "Something waits for a connection that is switched off.");

        }

        #endregion

    }

}
