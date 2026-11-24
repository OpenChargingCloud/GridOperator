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
using System.Net.Http.Headers;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.OpenADR;
using cloud.charging.open.protocols.WWCP.Node.Streams;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.protocols.WWCP.Node.Connections;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The local controllers that connect to the grid operator over a WebSocket
    /// of the REST API of OpenADR: who is let in, which VEN it is taken as,
    /// and that the tunnel leads to the VTN and to nothing else.
    /// </summary>
    public class VENTunnelTests : AGridOperatorTests
    {

        #region Data

        private IPPort  venPort;

        #endregion


        #region (private) AStartedGrid()

        /// <summary>
        /// A grid operator, started, with its WebSocket port of OpenADR on a
        /// port of its own, two grid connection points and a local controller
        /// behind each.
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
                                              new JProperty("clientAuthentication",  new JArray("httpAuthWithoutTLS"))
                                          ))
                                      ).ToString()
                                  );

                       });

            Assert.That(grid.VENServerRunning, Is.True, "The WebSocket server of OpenADR is switched on, and not running.");

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-2"), "test", out _, out error,     out _), Is.True, error);

            Assert.That(grid.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out _, out error, out _), Is.True, error);
            Assert.That(grid.TryAddLocalController(ALocalController("lc-nord",     "DE*GEF*P2", "GCP-2"), "test", out _, out _, out error, out _), Is.True, error);

            return grid;

        }

        #endregion

        #region (private) AVENThatConnects(Grid, Login, VEN, Binding = "rest", Enabled = true)

        /// <summary>
        /// A connection of a VEN that comes under the given login, and the
        /// password it signs in with.
        /// </summary>
        private String AVENThatConnects(GridOperator  Grid,
                                        String        Login,
                                        String        VEN,
                                        String        Binding  = "rest",
                                        Boolean       Enabled  = true)
        {

            var password = "a-password-" + Guid.NewGuid().ToString("N")[..12];

            Assert.That(Grid.Connections.TryAddConnection(I18NString.Create($"The local controller of '{VEN}'"),
                                                          URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/{Login}"),
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

        #region (private) Tunnel(Login, Password)

        /// <summary>
        /// A WebSocket of the REST API of OpenADR to the grid operator, signed
        /// in with the given password, and an HTTP client that asks through it -
        /// or the status the WebSocket was refused with.
        /// </summary>
        private async Task<(HttpClient? Client, HTTPStatusCode Status, StreamWebSocketClient WebSocket)> Tunnel(String  Login,
                                                                                                                String  Password)
        {

            var webSocket = new StreamWebSocketClient(
                                URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/{Login}"),
                                OpenADRWireNames.RestSubprotocol,
                                HTTPBasicAuthentication.Create(Login, Password),
                                null,
                                null,
                                null,
                                null,
                                null,
                                null
                            );

            var (_, response) = await webSocket.Connect();

            if (response.HTTPStatusCode != HTTPStatusCode.SwitchingProtocols)
                return (null, response.HTTPStatusCode, webSocket);

            Assert.That(response.SecWebSocketProtocol, Is.EqualTo(OpenADRWireNames.RestSubprotocol));

            var handed = 0;

            var client = new HttpClient(new SocketsHttpHandler {
                                            ConnectCallback          = (context, cancellationToken) =>
                                                                           Interlocked.Exchange(ref handed, 1) == 0
                                                                               ? ValueTask.FromResult<Stream>(webSocket.Stream)
                                                                               : throw new HttpRequestException("The tunnel is one connection, and it was given back."),
                                            MaxConnectionsPerServer  = 1,
                                            UseProxy                 = false
                                        }) {
                             BaseAddress  = new Uri("http://vtn.openadr.tunnel/"),
                             Timeout      = TimeSpan.FromSeconds(10)
                         };

            return (client, response.HTTPStatusCode, webSocket);

        }

        #endregion


        #region ATunnel_ReachesTheVTN_AsItsVEN_WithoutAToken()

        /// <summary>
        /// Through the tunnel the VTN answers as to the VEN the connection names,
        /// without a token: its own VEN, and nobody else's.
        /// </summary>
        [Test]
        public async Task ATunnel_ReachesTheVTN_AsItsVEN_WithoutAToken()
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "lc-1", "lc-paradies");

            var (client, status, webSocket) = await Tunnel("lc-1", password);

            Assert.That(client, Is.Not.Null, $"The WebSocket was refused: {status}.");

            using (client)
            await using (webSocket)
            {

                var vens = await client!.GetAsync("openadr3/vens");

                Assert.That(vens.StatusCode, Is.EqualTo(HttpStatusCode.OK));

                var listed = JArray.Parse(await vens.Content.ReadAsStringAsync());

                Assert.Multiple(() => {
                    Assert.That(listed,                                  Has.Count.EqualTo(1));
                    Assert.That(listed[0]?["venName"]?.Value<String>(),  Is.EqualTo("lc-paradies"));
                    Assert.That(grid.VENLoginsIn,                        Does.Contain("lc-1"));
                });

            }

        }

        #endregion

        #region ATunnel_LeadsToTheVTN_AndToNothingElse()

        /// <summary>
        /// Through the tunnel there is the VTN, and nothing else of the grid
        /// operator: neither its JSON API nor its web interface.
        /// </summary>
        [Test]
        public async Task ATunnel_LeadsToTheVTN_AndToNothingElse()
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "lc-1", "lc-paradies");

            var (client, status, webSocket) = await Tunnel("lc-1", password);

            Assert.That(client, Is.Not.Null, $"The WebSocket was refused: {status}.");

            using (client)
            await using (webSocket)
            {

                var api        = await client!.GetAsync("api/v1/status");
                var web        = await client.GetAsync("");
                var programs   = await client.GetAsync("openadr3/programs");

                Assert.Multiple(() => {
                    Assert.That(api.StatusCode,       Is.EqualTo(HttpStatusCode.NotFound),  "The JSON API of the grid operator answered through a tunnel.");
                    Assert.That(web.StatusCode,       Is.EqualTo(HttpStatusCode.NotFound),  "The web interface of the grid operator answered through a tunnel.");
                    Assert.That(programs.StatusCode,  Is.EqualTo(HttpStatusCode.OK),        "The VTN did not answer through the tunnel.");
                });

            }

        }

        #endregion

        #region ATunnel_IsItsVEN_WhateverTokenItShows()

        /// <summary>
        /// A bearer token of another VEN, shown through the tunnel, changes
        /// nothing: the tunnel is the VEN of its connection.
        /// </summary>
        [Test]
        public async Task ATunnel_IsItsVEN_WhateverTokenItShows()
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "lc-1", "lc-paradies");

            Assert.That(grid.TryNewSecret("lc-nord", "test", out _, out var nordSecret, out var error, out _), Is.True, error);

            var token     = grid.OpenADR.TryIssueToken("lc-nord", nordSecret!);

            Assert.That(token, Is.Not.Null, "No token for lc-nord.");

            var (client, status, webSocket) = await Tunnel("lc-1", password);

            Assert.That(client, Is.Not.Null, $"The WebSocket was refused: {status}.");

            using (client)
            await using (webSocket)
            {

                using var request = new HttpRequestMessage(HttpMethod.Get, "openadr3/vens");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token!.Value.Token);

                var vens   = await client!.SendAsync(request);
                var listed = JArray.Parse(await vens.Content.ReadAsStringAsync());

                Assert.That(listed[0]?["venName"]?.Value<String>(), Is.EqualTo("lc-paradies"), "A token of another VEN made the tunnel that VEN.");

            }

        }

        #endregion

        #region ATunnelOfNoLocalController_IsRefusedEverything()

        /// <summary>
        /// A connection that names a VEN that is no local controller of the grid
        /// operator - removed since, say - is let in, and told 401 to everything.
        /// </summary>
        [Test]
        public async Task ATunnelOfNoLocalController_IsRefusedEverything()
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "lc-1", "lc-nobody");

            var (client, status, webSocket) = await Tunnel("lc-1", password);

            Assert.That(client, Is.Not.Null, $"The WebSocket was refused: {status}.");

            using (client)
            await using (webSocket)
            {
                Assert.That((await client!.GetAsync("openadr3/events")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            }

        }

        #endregion

        #region WhatIsNotLetIn()

        /// <summary>
        /// A connection that is switched off, one of the binding of messages -
        /// not served yet -, a login of none and a wrong password are not let
        /// in; and the store writes down no VEN without the VEN it is taken as,
        /// none this grid operator would make, and nothing else.
        /// </summary>
        [Test]
        public async Task WhatIsNotLetIn()
        {

            var grid          = await AStartedGrid();

            var switchedOff   = AVENThatConnects(grid, "lc-off",       "lc-paradies", Enabled: false);
            var ofMessages    = AVENThatConnects(grid, "lc-messages",  "lc-paradies", Binding: "messages");
            var right         = AVENThatConnects(grid, "lc-1",         "lc-paradies");

            Assert.That(grid.WebSocketLogins.TrySetPasswordAuthentication(User_Id.Parse("lc-nowhere"), "a-password-of-nobody", null, I18NString.Create("nobody"), out var error), Is.True, error);

            foreach (var (login, password, what) in new[] { ("lc-off",      switchedOff,             "a connection that is switched off"),
                                                            ("lc-messages", ofMessages,              "a connection of the JSON messages"),
                                                            ("lc-nowhere",  "a-password-of-nobody",  "a login of no connection"),
                                                            ("lc-1",        "not-its-password",      "a wrong password") })
            {
                var (client, status, webSocket) = await Tunnel(login, password);
                client?.Dispose();
                await webSocket.DisposeAsync();
                Assert.That(client, Is.Null, $"{what} was let in ({status}).");
            }

            Assert.Multiple(() => {

                Assert.That(grid.Connections.TryAddConnection(I18NString.Create("no VEN said"), URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/lc-2"), "VEN", true, null, null,
                                                              out _, out var noVEN, Inbound: true, OpenADRBinding: "rest"),
                            Is.False, "A connection of a VEN was written down without the VEN it is taken as.");

                Assert.That(grid.Connections.TryAddConnection(I18NString.Create("outbound"), URL.Parse("ws://127.0.0.1:1/lc-2"), "VEN", true, null, null,
                                                              out _, out var outbound, OpenADRBinding: "rest"),
                            Is.False, "A grid operator connects to a VEN.");

                Assert.That(grid.Connections.TryAddConnection(I18NString.Create("another grid operator"), URL.Parse("ws://127.0.0.1:1/x"), "GridOperator", true, null, null,
                                                              out _, out var gridOperator, OpenADRBinding: "rest"),
                            Is.False, "A grid operator connects to another one.");

                Assert.That(grid.Connections.TryAddConnection(I18NString.Create("another binding"), URL.Parse($"ws://127.0.0.1:{venPort.ToUInt16()}/lc-3"), "VEN", true, null, null,
                                                              out _, out var binding, Inbound: true, OpenADRBinding: "soap", VEN: "lc-nord"),
                            Is.False, "A binding of OpenADR that is none was taken.");

            });

            // And the right one, still.
            var (rightClient, rightStatus, rightWebSocket) = await Tunnel("lc-1", right);
            rightClient?.Dispose();
            await rightWebSocket.DisposeAsync();
            Assert.That(rightClient, Is.Not.Null, $"The connection written down was refused: {rightStatus}.");

        }

        #endregion


        #region ATunnelNoLongerWhatLetItIn_IsLetGoOf(Change)

        /// <summary>
        /// A tunnel in over its connection is let go of once the connection is no
        /// longer what let it in - switched off, written down for the messages,
        /// removed -, and is not let in again.
        /// </summary>
        [TestCase("off")]
        [TestCase("messages")]
        [TestCase("removed")]
        public async Task ATunnelNoLongerWhatLetItIn_IsLetGoOf(String Change)
        {

            var grid      = await AStartedGrid();
            var password  = AVENThatConnects(grid, "lc-1", "lc-paradies");

            var (client, status, webSocket) = await Tunnel("lc-1", password);

            await using (webSocket)
            using (client)
            {

                Assert.That(client, Is.Not.Null, $"The tunnel was refused: {status}.");

                var programs = await client!.GetAsync("openadr3/programs");

                Assert.That(programs.IsSuccessStatusCode, Is.True, $"The VTN answered {programs.StatusCode} through the tunnel.");
                Assert.That(grid.VENLoginsIn, Does.Contain("lc-1"));

                VENMessagesTests.ChangeTheConnectionOf(grid, "lc-1", Change);

                var until = DateTimeOffset.UtcNow.AddSeconds(10);

                while (grid.VENLoginsIn.Contains("lc-1") && DateTimeOffset.UtcNow < until)
                    await Task.Delay(50);

                Assert.That(grid.VENLoginsIn, Does.Not.Contain("lc-1"), $"The tunnel was not let go of, its connection {Change}.");

                Assert.That(async () => await client.GetAsync("openadr3/programs"), Throws.Exception, "The tunnel still answers.");

            }

            var (again, againStatus, againWebSocket) = await Tunnel("lc-1", password);
            again?.Dispose();
            await againWebSocket.DisposeAsync();

            Assert.That(again, Is.Null, $"The tunnel was let in again, its connection {Change} ({againStatus}).");

        }

        #endregion

    }

}
