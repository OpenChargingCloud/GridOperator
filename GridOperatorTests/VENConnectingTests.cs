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

using System.Net;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.SSH;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.SSH.Client;

using cloud.charging.open.protocols.OpenADRv3.Messaging;
using cloud.charging.open.protocols.WWCP.Node.OpenADR;
using cloud.charging.open.protocols.WWCP.Node.Streams;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.GridOperator.CommandLine;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// A local controller let connect over the WebSocket port of OpenADR or
    /// over SSH - by the JSON API and at the command line, as an operator of
    /// the grid does it: a connection and a login written down, a password said
    /// once or an SSH key taken, and with them it comes in; taken away, or its
    /// local controller removed, it does not.
    /// </summary>
    public class VENConnectingTests : AGridOperatorTests
    {

        #region Data

        /// <summary>
        /// The public key of a local controller, as one line of an authorized_keys file.
        /// </summary>
        private const String AKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIFPDfYBRyC5TX/T7HwbaojyczgPaoqc2pQwMTVmXT9Ye lc-test";

        private IPPort venPort;

        #endregion


        #region (private) AStartedGrid(WebSocketServer = true)

        /// <summary>
        /// A grid operator, started - with its WebSocket port of OpenADR and its
        /// SSH server on ports of their own and OpenADR over SSH switched on,
        /// unless said otherwise -, a grid connection point and a local
        /// controller behind it.
        /// </summary>
        private async Task<GridOperator> AStartedGrid(Boolean WebSocketServer = true)
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => {

                           venPort = TestPorts.Free();

                           var configuration = new JObject(
                                                   new JProperty("nts",           new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))))),
                                                   new JProperty("gridOperator",  new JObject(new JProperty("@id", "DE*SWJ"), new JProperty("name", "Netze Beispielstadt")))
                                               );

                           if (WebSocketServer)
                           {
                               configuration.Add(new JProperty("webSocketServer", new JObject(
                                                     new JProperty("enabled",               true),
                                                     new JProperty("address",               "127.0.0.1"),
                                                     new JProperty("port",                  venPort.ToUInt16()),
                                                     new JProperty("clientAuthentication",  new JArray("httpAuthWithoutTLS")),
                                                     new JProperty("ssh",                   new JObject(new JProperty("enabled", true)))
                                                 )));
                               configuration.Add(new JProperty("ssh", new JObject(
                                                     new JProperty("enabled",  true),
                                                     new JProperty("port",     TestPorts.Free().ToUInt16())
                                                 )));
                           }

                           return GridOperatorFrom(configuration.ToString());

                       });

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(grid.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out _, out error, out _), Is.True, error);

            return grid;

        }

        #endregion

        #region (private) InOverTheWebSocket(Login, Password)

        /// <summary>
        /// Whether a VEN comes in at the WebSocket port of OpenADR under the
        /// given login with the given password.
        /// </summary>
        private async Task<Boolean> InOverTheWebSocket(String Login, String Password)
        {

            var webSocket = new MessageWebSocketClient(
                                URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/{Login}"),
                                OpenADRWireNames.Subprotocol,
                                HTTPBasicAuthentication.Create(Login, Password),
                                null,
                                null,
                                null,
                                null,
                                null,
                                null
                            );

            try
            {
                var (_, response) = await webSocket.Connect();
                return response.HTTPStatusCode == HTTPStatusCode.SwitchingProtocols;
            }
            catch
            {
                return false;
            }
            finally
            {
                webSocket.End();
                await webSocket.DisposeAsync();
            }

        }

        #endregion

        #region (private) InOverSSH(Grid, Login, Password)

        /// <summary>
        /// Whether a VEN comes in over the SSH subsystem of OpenADR under the
        /// given login with the given password.
        /// </summary>
        private static async Task<Boolean> InOverSSH(GridOperator Grid, String Login, String Password)
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
                return false;
            }

            var client = new SshMessageClient(new SshMessageClientOptions {
                             Host          = "127.0.0.1",
                             Connection    = _ => ValueTask.FromResult(ssh),
                             Subsystem     = OpenADRWireNames.SshSubsystem,
                             Subprotocols  = [ OpenADRWireNames.Subprotocol ]
                         });

            try
            {
                await client.ConnectAsync();
                return client.IsConnected;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { await client.Close(); } catch { }
                try { await client.DisposeAsync(); } catch { }
                await ssh.DisposeAsync();
            }

        }

        #endregion


        #region OverTheJSONAPIALocalControllerConnectsOverEachWay()

        /// <summary>
        /// An administrator lets the local controller connect over the WebSocket
        /// port and over SSH: each answer says the URL the local controller
        /// connects to and the password it signs in with, the one time - and
        /// with it, it comes in. A viewer lets nothing connect.
        /// </summary>
        [Test]
        public async Task OverTheJSONAPIALocalControllerConnectsOverEachWay()
        {

            var grid = await AStartedGrid();

            using var admin   = await SignedInAs(User_Id.Parse("admin1"),  "systemadmin");
            using var viewer  = await SignedInAs(User_Id.Parse("viewer1"), "viewer");

            var (status, _)  = await Send(viewer, HttpMethod.Post, "api/v1/openADR/localControllers/lc-paradies/connections", new JObject(new JProperty("transport", "ws")));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden));

            #region Over the WebSocket port, in messages

            (status, var json) = await Send(admin, HttpMethod.Post, "api/v1/openADR/localControllers/lc-paradies/connections", new JObject(new JProperty("transport", "ws")));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), json.ToString());

            var ws = json["credentials"]!;

            Assert.Multiple(() => {
                Assert.That(json["venName"]?.Value<String>(),     Is.EqualTo("lc-paradies"));
                Assert.That(json["transport"]?.Value<String>(),   Is.EqualTo("ws"));
                Assert.That(json["binding"]?.Value<String>(),     Is.EqualTo("messages"));
                Assert.That(ws["vtnURL"]?.Value<String>(),        Is.EqualTo($"ws://127.0.0.1:{venPort.ToUInt16()}/lc-paradies"));
                Assert.That(ws["openADRBinding"]?.Value<String>(), Is.EqualTo("messages"));
                Assert.That(ws["login"]?.Value<String>(),         Is.EqualTo("lc-paradies"));
                Assert.That(ws["password"]?.Value<String>(),      Has.Length.GreaterThanOrEqualTo(16));
                Assert.That(ws["sshHostKey"],                     Is.Null);
            });

            Assert.That(await InOverTheWebSocket("lc-paradies", ws["password"]!.Value<String>()!), Is.True,  "the password said did not let it in");
            Assert.That(await InOverTheWebSocket("lc-paradies", "not-the-password-at-all"),      Is.False, "a wrong password let it in");

            #endregion

            #region Over SSH, in the REST binding: a login of its own

            (status, json) = await Send(admin, HttpMethod.Post, "api/v1/openADR/localControllers/lc-paradies/connections", new JObject(new JProperty("transport", "ssh"), new JProperty("binding", "rest")));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), json.ToString());

            var ssh = json["credentials"]!;

            Assert.Multiple(() => {
                Assert.That(json["transport"]?.Value<String>(),   Is.EqualTo("ssh"));
                Assert.That(ssh["vtnURL"]?.Value<String>(),       Is.EqualTo($"ssh://lc-paradies-ssh@127.0.0.1:{grid.SSHPort!.Value.ToUInt16()}"));
                Assert.That(ssh["openADRBinding"]?.Value<String>(), Is.EqualTo("rest"));
                Assert.That(ssh["login"]?.Value<String>(),        Is.EqualTo("lc-paradies-ssh"));
                Assert.That(ssh["sshHostKey"]?.Value<String>(),   Does.StartWith("SHA256:"));
                Assert.That(grid.SSHHostKey,                      Does.EndWith(ssh["sshHostKey"]!.Value<String>()!));
            });

            #endregion

            #region Both are its connections now, without their passwords

            (status, json) = await Send(admin, HttpMethod.Get, "api/v1/openADR/localControllers");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));

            Assert.That(json["connectable"]?["ws"]?.Value<Boolean>(),  Is.True);
            Assert.That(json["connectable"]?["ssh"]?.Value<Boolean>(), Is.True);

            var connections = json["localControllers"]![0]!["connections"] as JArray;

            Assert.That(connections?.Select(connection => connection["login"]?.Value<String>()), Is.EquivalentTo(new[] { "lc-paradies", "lc-paradies-ssh" }));
            Assert.That(json.ToString(), Does.Not.Contain(ws["password"]!.Value<String>()!));

            #endregion

        }

        #endregion

        #region OverSSHThePasswordSaidLetsItIn()

        /// <summary>
        /// Over SSH, in the binding of messages, the password said lets the
        /// local controller into the subsystem of OpenADR - and what its
        /// login says calls it a local controller, not a charging station.
        /// </summary>
        [Test]
        public async Task OverSSHThePasswordSaidLetsItIn()
        {

            var grid = await AStartedGrid();
            var said = new List<String>();

            grid.WebSocketLogins.OnNotice += (_, message) => said.Add(message);

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ssh")), "test", null, out var made, out var error, out _), Is.True, error);

            Assert.That(said, Has.Some.StartsWith("The local controller 'lc-paradies' may now sign in"));
            Assert.That(said, Has.None.Contains("charging station"));

            Assert.That(made.Login,    Is.EqualTo("lc-paradies"));
            Assert.That(made.Password, Is.Not.Null);

            Assert.That(await InOverSSH(grid, made.Login, made.Password!),      Is.True,  "the password said did not let it in");
            Assert.That(await InOverSSH(grid, made.Login, "not-the-password"),  Is.False, "a wrong password let it in");

        }

        #endregion

        #region AnSSHKeyIsTakenRatherThanAPassword()

        /// <summary>
        /// Over SSH a local controller may bring its key: no password is made,
        /// and the key is its login's. Over the WebSocket port it may not.
        /// </summary>
        [Test]
        public async Task AnSSHKeyIsTakenRatherThanAPassword()
        {

            var grid = await AStartedGrid();

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ws"), new JProperty("sshKey", AKey)), "test", null, out _, out var error, out var refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid), error);

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ssh"), new JProperty("sshKey", AKey)), "test", null, out var made, out error, out _), Is.True, error);

            Assert.That(made.Password, Is.Null);
            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse(made.Login), out var login), Is.True);
            Assert.That(login!.ToString() + " " + grid.WebSocketLogins.ToJSON().ToString(), Does.Contain("AAAAC3NzaC1lZDI1NTE5AAAAIFPDfYBRyC5TX"));

        }

        #endregion

        #region TakenAwayItComesInNoMore()

        /// <summary>
        /// A connection taken away takes the login it came under with it: the
        /// password said no longer lets anything in.
        /// </summary>
        [Test]
        public async Task TakenAwayItComesInNoMore()
        {

            var grid = await AStartedGrid();

            using var admin = await SignedInAs(User_Id.Parse("admin1"), "systemadmin");

            var (status, json) = await Send(admin, HttpMethod.Post, "api/v1/openADR/localControllers/lc-paradies/connections", new JObject(new JProperty("transport", "ws")));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), json.ToString());

            var id        = json["id"]!.Value<String>()!;
            var password  = json["credentials"]!["password"]!.Value<String>()!;

            Assert.That(await InOverTheWebSocket("lc-paradies", password), Is.True);

            (status, _) = await Send(admin, HttpMethod.Delete, "api/v1/openADR/localControllers/lc-paradies/connections/nothing");
            Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound));

            (status, json) = await Send(admin, HttpMethod.Delete, $"api/v1/openADR/localControllers/lc-paradies/connections/{Uri.EscapeDataString(id)}");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), json.ToString());

            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-paradies"), out _), Is.False, "the login stayed");
            Assert.That(await InOverTheWebSocket("lc-paradies", password), Is.False, "it still came in");

        }

        #endregion

        #region RemovedItsConnectionsGoWithIt()

        /// <summary>
        /// A local controller removed takes its connections and their logins
        /// with it.
        /// </summary>
        [Test]
        public async Task RemovedItsConnectionsGoWithIt()
        {

            var grid = await AStartedGrid();

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ws")),  "test", null, out _, out var error, out _), Is.True, error);
            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ssh")), "test", null, out _, out error,     out _), Is.True, error);

            Assert.That(grid.VENConnectionsText("lc-paradies"), Has.Count.EqualTo(2));

            Assert.That(grid.TryRemoveLocalController("lc-paradies", "test", out error, out _), Is.True, error);

            Assert.That(grid.Connections.Connections,                                     Is.Empty);
            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-paradies"),     out _), Is.False);
            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-paradies-ssh"), out _), Is.False);

        }

        #endregion

        #region WhatCannotBeIsRefused()

        /// <summary>
        /// No local controller, no transport, no binding, a login somebody has:
        /// each refused, and nothing written down.
        /// </summary>
        [Test]
        public async Task WhatCannotBeIsRefused()
        {

            var grid = await AStartedGrid();

            Assert.That(grid.TryConnectLocalController("lc-nobody",   new JObject(new JProperty("transport", "ws")),                                       "test", null, out _, out _, out var refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.NotFound));

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "soap")),                                     "test", null, out _, out _, out refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ws"), new JProperty("binding", "carrier pigeon")), "test", null, out _, out _, out refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ws")),                                       "test", null, out _, out var error, out _), Is.True, error);

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ssh"), new JProperty("login", "lc-paradies")), "test", null, out _, out _, out refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.Conflict));

            Assert.That(grid.Connections.Connections, Has.Count.EqualTo(1));

        }

        #endregion

        #region WithoutTheWebSocketServerNothingConnects()

        /// <summary>
        /// A grid operator whose WebSocket server of OpenADR is switched off
        /// says so, rather than writing down what nothing would connect to.
        /// </summary>
        [Test]
        public async Task WithoutTheWebSocketServerNothingConnects()
        {

            var grid = await AStartedGrid(WebSocketServer: false);

            Assert.That(grid.VENConnectableJSON().ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("""{"ws":false,"ssh":false}"""));

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ws")),  "test", null, out _, out var error, out var refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.Conflict));
            Assert.That(error,   Does.Contain("webSocketServer.enabled"));

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ssh")), "test", null, out _, out error,     out refusal), Is.False);
            Assert.That(refusal, Is.EqualTo(ChangeRefusal.Conflict));

            Assert.That(grid.Connections.Connections, Is.Empty);

        }

        #endregion

        #region AtTheCommandLine()

        /// <summary>
        /// "localControllers connect" says the URL and the password once, the
        /// list says what it connects over, "disconnect" takes it away.
        /// </summary>
        [Test]
        public async Task AtTheCommandLine()
        {

            var       grid  = await AStartedGrid();
            using var cli   = new GridOperatorCLI(grid);

            var said = String.Join("\n", await cli.Execute("localControllers connect lc-paradies ws rest"));

            Assert.That(said, Does.Contain($"ws://127.0.0.1:{venPort.ToUInt16()}/lc-paradies"), said);
            Assert.That(said, Does.Contain("password"));
            Assert.That(said, Does.Contain("binding of rest"));

            var keyed = String.Join("\n", await cli.Execute($"localControllers connect lc-paradies ssh {AKey}"));

            Assert.That(keyed, Does.Contain("with its SSH key"), keyed);
            Assert.That(keyed, Does.Contain("host key  SHA256:"));

            var list = String.Join("\n", await cli.Execute("localControllers"));

            Assert.That(list, Does.Contain("connects over "),  list);
            Assert.That(list, Does.Contain("(ws, rest)"),      list);
            Assert.That(list, Does.Contain("(ssh, messages)"), list);

            var id = grid.Connections.Connections.First(connection => !connection.IsSSH).Id;

            Assert.That(String.Join("\n", await cli.Execute($"localControllers disconnect lc-paradies {id}")), Does.Contain("is taken away"));
            Assert.That(String.Join("\n", await cli.Execute("localControllers connect lc-paradies")),          Does.StartWith("Usage:"));

            Assert.That(grid.Connections.Connections, Has.Count.EqualTo(1));

        }

        #endregion

    }

}
