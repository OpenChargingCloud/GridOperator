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
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.OpenADR;
using cloud.charging.open.protocols.WWCP.Node.Streams;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.GridOperator.CommandLine;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The local controllers that connect over a WebSocket or SSH, over the
    /// JSON API and at the command line of a grid operator: their connections,
    /// their logins and what they sign in with, at the routes every node has,
    /// below "v1/configuration/ven-server" and at the resource "openadr" -
    /// which an administrator may change, and a planner and a dispatcher only
    /// read.
    /// </summary>
    public class VENServerAPITests : AGridOperatorTests
    {

        #region Data

        private IPPort venPort;

        #endregion


        #region (private) AGridWithItsVENServer()

        /// <summary>
        /// A grid operator, started, with its WebSocket server of OpenADR
        /// listening on a port of its own, and a local controller of HTTPS.
        /// </summary>
        private async Task<GridOperator> AGridWithItsVENServer()
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

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(grid.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out _, out error, out _), Is.True, error);

            return grid;

        }

        #endregion

        #region (private static) LoginIn(Logins, Login)

        /// <summary>
        /// The login of the given name, as the list of the logins has it.
        /// </summary>
        private static JObject? LoginIn(JToken Logins, String Login)

            => (Logins["logins"] as JArray)?.OfType<JObject>().FirstOrDefault(login => login.Value<String>("login") == Login);

        #endregion

        #region (private) SignsIn(Login, Password)

        /// <summary>
        /// Whether a WebSocket of the REST API of OpenADR is let in with the given password.
        /// </summary>
        private async Task<HTTPStatusCode> SignsIn(String Login, String Password)
        {

            await using var webSocket = new StreamWebSocketClient(
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

            return response.HTTPStatusCode;

        }

        #endregion


        #region AnAdministratorLetsALocalControllerIn()

        /// <summary>
        /// An administrator writes down a local controller - its connection,
        /// inbound, of the type "VEN" and taken as a VEN, and its login with a
        /// password -, and it connects; switched off, it does not; with a key
        /// pin beside, and forgotten again. The server it connects to is read
        /// beside.
        /// </summary>
        [Test]
        public async Task AnAdministratorLetsALocalControllerIn()
        {

            var grid = await AGridWithItsVENServer();

            using var admin = await SignedInAs(User_Id.Parse("admin1"), "systemadmin");

            #region The server, with nobody in yet

            var (status, server) = await Send(admin, HttpMethod.Get, "api/v1/configuration/ven-server");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), server.ToString());

            Assert.Multiple(() => {
                Assert.That(server["configuration"],                        Is.InstanceOf<JObject>());
                Assert.That(server["state"]?["running"]?.Value<Boolean>(),  Is.True);
                Assert.That(server["state"]?["tls"]?.Value<Boolean>(),      Is.False);
                Assert.That(server["connected"] as JArray,                  Is.Empty);
            });

            #endregion

            #region Its connection

            (status, var added) = await Send(admin, HttpMethod.Post, "api/v1/configuration/connections", new JObject(
                                      new JProperty("description",     new JObject(new JProperty("en", "The local controller of the Paradies"))),
                                      new JProperty("url",             $"ws://127.0.0.1:{venPort.ToUInt16()}/lc-1"),
                                      new JProperty("connectionType",  "VEN"),
                                      new JProperty("autoConnect",     true),
                                      new JProperty("inbound",         true),
                                      new JProperty("openADRBinding",  "rest"),
                                      new JProperty("ven",             "lc-paradies")
                                  ));

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), added.ToString());

            var connectionId = added["id"]!.Value<String>()!;

            Assert.That(grid.Connections.Connections.Single(entry => entry.Id == connectionId).InboundName, Is.EqualTo("lc-1"));

            #endregion

            #region Its login, with a password - and it signs in

            var password = "a-long-password-" + Guid.NewGuid().ToString("N");

            (status, var logins) = await Send(admin, HttpMethod.Post, "api/v1/configuration/ven-server/logins", JObject.FromObject(new {
                                       login     = "lc-1",
                                       password
                                   }));

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), logins.ToString());
            Assert.That(LoginIn(logins["logins"]!, "lc-1"), Is.Not.Null, logins.ToString());

            Assert.That(await SignsIn("lc-1", password),        Is.EqualTo(HTTPStatusCode.SwitchingProtocols), "the local controller written down over HTTP was not let in");
            Assert.That(await SignsIn("lc-1", "a-wrong-one"),   Is.Not.EqualTo(HTTPStatusCode.SwitchingProtocols));

            #endregion

            #region A key pin beside

            var pin = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("the public key of the local controller")));

            (status, var pinned) = await Send(admin, HttpMethod.Post, "api/v1/configuration/ven-server/logins/lc-1/keyPins", JObject.FromObject(new { pin, label = "its TLS key" }));

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.OK), pinned.ToString());
            Assert.That(pinned["keyPin"]?.Value<String>(),  Is.EqualTo(pin).IgnoreCase);

            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-1"), out var login), Is.True);

            Assert.Multiple(() => {
                Assert.That(login!.HasPassword,  Is.True);
                Assert.That(login.HasKeyPin,     Is.True);
                Assert.That(login.Enabled,       Is.True);
            });

            #endregion

            #region Switched off - and no mark of OCPP -, and on again

            (status, var off) = await Send(admin, HttpMethod.Put, "api/v1/configuration/ven-server/logins/lc-1", JObject.FromObject(new { enabled = false }));

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), off.ToString());
            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-1"), out login) && !login.Enabled, Is.True);
            Assert.That(await SignsIn("lc-1", password), Is.Not.EqualTo(HTTPStatusCode.SwitchingProtocols), "a login switched off was let in");

            (status, var marked) = await Send(admin, HttpMethod.Put, "api/v1/configuration/ven-server/logins/lc-1", JObject.FromObject(new { localController = true }));

            Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest), marked.ToString());
            Assert.That(marked["description"]?.ToString() ?? marked.ToString(), Does.Contain("'enabled'").And.Not.Contain("localController"));

            (status, var on) = await Send(admin, HttpMethod.Put, "api/v1/configuration/ven-server/logins/lc-1", JObject.FromObject(new { enabled = true }));

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), on.ToString());

            #endregion

            #region The chains a client certificate may lead to

            (status, var trust) = await Send(admin, HttpMethod.Get, "api/v1/configuration/ven-server/trust");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), trust.ToString());

            #endregion

            #region Forgotten again

            (status, var forgotten) = await Send(admin, HttpMethod.Delete, "api/v1/configuration/ven-server/logins/lc-1");

            Assert.That(status,                                                    Is.EqualTo(HttpStatusCode.OK), forgotten.ToString());
            Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-1"), out _), Is.False);

            (status, var removed) = await Send(admin, HttpMethod.Post, "api/v1/configuration/connections/remove", JObject.FromObject(new { id = connectionId }));

            Assert.That(status,                                                              Is.EqualTo(HttpStatusCode.OK), removed.ToString());
            Assert.That(grid.Connections.Connections.Any(entry => entry.Id == connectionId), Is.False);

            #endregion

        }

        #endregion

        #region APlannerAndADispatcherOnlyRead(Role)

        /// <summary>
        /// Who may let a local controller in decides who is sent the limits of
        /// a grid connection point: a planner and a dispatcher read the local
        /// controllers that connect, and change nothing of them.
        /// </summary>
        [TestCase("planner")]
        [TestCase("dispatcher")]
        public async Task APlannerAndADispatcherOnlyRead(String Role)
        {

            var grid = await AGridWithItsVENServer();

            using var user = await SignedInAs(User_Id.Parse(Role + "1"), Role);

            var (status, server) = await Send(user, HttpMethod.Get, "api/v1/configuration/ven-server");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), server.ToString());

            (status, var logins) = await Send(user, HttpMethod.Get, "api/v1/configuration/ven-server/logins");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), logins.ToString());

            (status, var connections) = await Send(user, HttpMethod.Get, "api/v1/configuration/connections");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), connections.ToString());

            (status, var login) = await Send(user, HttpMethod.Post, "api/v1/configuration/ven-server/logins", JObject.FromObject(new {
                                      login     = "lc-1",
                                      password  = "a-long-password-" + Guid.NewGuid().ToString("N")
                                  }));

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), login.ToString());

            (status, var connection) = await Send(user, HttpMethod.Post, "api/v1/configuration/connections", new JObject(
                                           new JProperty("url",             $"ws://127.0.0.1:{venPort.ToUInt16()}/lc-1"),
                                           new JProperty("connectionType",  "VEN"),
                                           new JProperty("inbound",         true),
                                           new JProperty("openADRBinding",  "rest"),
                                           new JProperty("ven",             "lc-paradies")
                                       ));

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), connection.ToString());

            Assert.Multiple(() => {
                Assert.That(grid.WebSocketLogins.TryGet(User_Id.Parse("lc-1"), out _),  Is.False);
                Assert.That(grid.Connections.Connections,                               Is.Empty);
            });

        }

        #endregion

        #region AtTheConsoleVensListsTheLocalControllers()

        /// <summary>
        /// 'vens' says where the server is and lists every local controller
        /// that connects, with its connection, its VEN and what it signs in
        /// with; 'venPins' and 'venKeys' give and list its key pins and keys,
        /// and say what keeps it out.
        /// </summary>
        [Test]
        public async Task AtTheConsoleVensListsTheLocalControllers()
        {

            var grid = GridOperatorFrom();

            using var cli = new GridOperatorCLI(grid);

            var none = await cli.Execute("vens");

            Assert.Multiple(() => {
                Assert.That(none, Has.Some.Contains("switched off"));
                Assert.That(none, Has.Some.EqualTo("No local controller is written down to connect: none may."));
            });

            Assert.That(grid.Connections.TryAddConnection(I18NString.Create("The local controller of the Paradies"),
                                                          URL.Parse("ws://127.0.0.1:2365/lc-1"),
                                                          "VEN",
                                                          true,
                                                          null,
                                                          null,
                                                          out _,
                                                          out var error,
                                                          Inbound:         true,
                                                          OpenADRBinding:  "messages",
                                                          VEN:             "lc-paradies"),
                        Is.True, error);

            var pin    = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("the public key of the local controller")));
            var given  = await cli.Execute($"venPins lc-1 add {pin} its-TLS-key");

            Assert.That(String.Join(" | ", given), Does.Contain("is a local controller of this").And.Contain("key pin"));

            var listed = await cli.Execute("vens");

            Assert.Multiple(() => {
                Assert.That(listed, Has.Some.StartsWith("lc-1  on  not connected  the VEN 'lc-paradies', in messages"));
                Assert.That(listed, Has.Some.Contains("signs in with 1 key pin"));
            });

            var pins   = await cli.Execute("venPins");

            Assert.Multiple(() => {
                Assert.That(pins, Has.Some.EqualTo("lc-1:"));
                Assert.That(pins, Has.Some.Contains("WebSocket server of OpenADR is switched off"));
            });

            var keys   = await cli.Execute("venKeys");

            Assert.That(keys, Has.Some.EqualTo("No local controller of this grid operator has an SSH key."));

        }

        #endregion

    }

}
