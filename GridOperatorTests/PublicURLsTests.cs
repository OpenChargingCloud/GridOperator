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

using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.GridOperator.Configuration;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// Where the local controllers out there reach a grid operator behind a
    /// reverse proxy, below a path of the proxy's, or behind a port forward of
    /// SSH: what "publicURLs" of its configuration says is what they are told -
    /// over the JSON API whatever Host it was asked at, and at the command line
    /// -, and what it does not say is made as before.
    /// </summary>
    public class PublicURLsTests : AGridOperatorTests
    {

        #region (private) AStartedGrid(PublicURLs)

        /// <summary>
        /// A grid operator, started with the given public URLs - its WebSocket
        /// port of OpenADR and its SSH server on ports of their own, OpenADR over
        /// SSH switched on -, a grid connection point and a local controller
        /// behind it.
        /// </summary>
        private async Task<GridOperator> AStartedGrid(JObject? PublicURLs)
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => {

                           var configuration = new JObject(
                                                   new JProperty("nts",              new JObject(new JProperty("groups", new JObject(new JProperty("legal", new JObject(new JProperty("enabled", false))))))),
                                                   new JProperty("gridOperator",     new JObject(new JProperty("@id", "DE*SWJ"), new JProperty("name", "Netze Beispielstadt"))),
                                                   new JProperty("webSocketServer",  new JObject(
                                                                                         new JProperty("enabled",               true),
                                                                                         new JProperty("address",               "127.0.0.1"),
                                                                                         new JProperty("port",                  TestPorts.Free().ToUInt16()),
                                                                                         new JProperty("clientAuthentication",  new JArray("httpAuthWithoutTLS")),
                                                                                         new JProperty("ssh",                   new JObject(new JProperty("enabled", true)))
                                                                                     )),
                                                   new JProperty("ssh",              new JObject(
                                                                                         new JProperty("enabled",  true),
                                                                                         new JProperty("port",     TestPorts.Free().ToUInt16())
                                                                                     ))
                                               );

                           if (PublicURLs is not null)
                               configuration.Add(new JProperty(PublicURLsConfiguration.SectionName, PublicURLs));

                           return GridOperatorFrom(configuration.ToString());

                       });

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);

            return grid;

        }

        #endregion


        #region WhatIsSaidIsWhatTheyAreTold()

        /// <summary>
        /// Behind a proxy that serves the grid operator at
        /// https://grid.example/dso/, takes the WebSockets of OpenADR on 443
        /// below /dso/openadr-ws/ and forwards 2222 to its SSH port: the VTN,
        /// its token and each connection are said there - though the request
        /// came to 127.0.0.1, and at the command line, where none came at all.
        /// </summary>
        [Test]
        public async Task WhatIsSaidIsWhatTheyAreTold()
        {

            var grid = await AStartedGrid(new JObject(
                                              new JProperty("openADR",    "https://grid.example/dso/openadr3"),
                                              new JProperty("webSocket",  "wss://grid.example/dso/openadr-ws"),
                                              new JProperty("ssh",        "ssh://grid.example:2222")
                                          ));

            using var admin = await SignedInAs(User_Id.Parse("admin1"), "systemadmin");

            var (status, json) = await Send(admin, HttpMethod.Post, "api/v1/openADR/localControllers", ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"));
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.Created), json.ToString());
            Assert.That(json["credentials"]?["vtnURL"]?.Value<String>(),        Is.EqualTo("https://grid.example/dso/openadr3/"));
            Assert.That(json["credentials"]?["tokenURL"]?.Value<String>(),      Is.EqualTo("https://grid.example/dso/openadr3/auth/token"));

            (status, json) = await Send(admin, HttpMethod.Post, "api/v1/openADR/localControllers/lc-paradies/connections", JObject.Parse("""{ "transport": "ws" }"""));
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.Created), json.ToString());
            Assert.That(json["credentials"]?["vtnURL"]?.Value<String>(),        Is.EqualTo("wss://grid.example/dso/openadr-ws/lc-paradies"));

            (status, json) = await Send(admin, HttpMethod.Post, "api/v1/openADR/localControllers/lc-paradies/connections", JObject.Parse("""{ "transport": "ssh" }"""));
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.Created), json.ToString());
            Assert.That(json["credentials"]?["vtnURL"]?.Value<String>(),        Is.EqualTo("ssh://lc-paradies-ssh@grid.example:2222"));

            Assert.That(grid.OpenADRVTNURLFor(null),    Is.EqualTo("https://grid.example/dso/openadr3/"));
            Assert.That(grid.OpenADRTokenURLFor(null),  Is.EqualTo("https://grid.example/dso/openadr3/auth/token"));

        }

        #endregion

        #region WhatIsNotSaidIsMadeAsBefore()

        /// <summary>
        /// Only SSH is forwarded: the VTN and the WebSocket port are where the
        /// request reached them, on the port the server listens on.
        /// </summary>
        [Test]
        public async Task WhatIsNotSaidIsMadeAsBefore()
        {

            var grid = await AStartedGrid(new JObject(new JProperty("ssh", "ssh://grid.example:2222")));

            Assert.That(grid.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out _, out var error, out _), Is.True, error);

            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ws")),  "test", null, out var overWS,  out error, out _), Is.True, error);
            Assert.That(grid.TryConnectLocalController("lc-paradies", new JObject(new JProperty("transport", "ssh")), "test", null, out var overSSH, out error, out _), Is.True, error);

            Assert.That(overWS!.URL,                    Is.EqualTo($"ws://127.0.0.1:{grid.VENServerSettings.TCPPort!.Value.ToUInt16()}/lc-paradies"));
            Assert.That(overSSH!.URL,                   Is.EqualTo("ssh://lc-paradies-ssh@grid.example:2222"));
            Assert.That(grid.OpenADRVTNURLFor(null),    Is.EqualTo(grid.OpenADRVTNURL));

        }

        #endregion

        #region WhatIsNoURLOfItsKindStopsTheStart()

        /// <summary>
        /// A URL of the wrong kind, with a user, a query, or a path where SSH has
        /// none, is refused with what is wrong with it - and a grid operator
        /// whose configuration says one does not start.
        /// </summary>
        [Test]
        public void WhatIsNoURLOfItsKindStopsTheStart()
        {

            foreach (var (wrong, says) in new (String, String)[] {
                         ("""{ "openADR":   "ftp://grid.example/" }""",          "'publicURLs.openADR' must be a http:// or https:// URL"),
                         ("""{ "openADR":   "grid.example/openadr3/" }""",       "'publicURLs.openADR' must be a http:// or https:// URL"),
                         ("""{ "openADR":   42 }""",                             "'publicURLs.openADR' must be a URL"),
                         ("""{ "webSocket": "https://grid.example/ws/" }""",     "'publicURLs.webSocket' must be a ws:// or wss:// URL"),
                         ("""{ "webSocket": "wss://grid.example/ws/?a=1" }""",   "'publicURLs.webSocket' has no query and no fragment"),
                         ("""{ "ssh":       "ssh://root@grid.example:2222" }""", "'publicURLs.ssh' names no user"),
                         ("""{ "ssh":       "ssh://grid.example:2222/dso" }""",  "'publicURLs.ssh' has no path")
                     })
            {
                Assert.That(PublicURLsConfiguration.TryParse(JObject.Parse(wrong), out _, out var error), Is.False, wrong);
                Assert.That(error, Does.StartWith(says), wrong);
            }

            Assert.That(PublicURLsConfiguration.TryParse(JObject.Parse("""{ "openADR": "HTTPS://grid.example:8443/dso/openadr3", "ssh": "ssh://grid.example/" }"""), out var configuration, out var notTaken), Is.True, notTaken);
            Assert.That(configuration!.OpenADR,    Is.EqualTo("https://grid.example:8443/dso/openadr3/"));
            Assert.That(configuration.SSH,         Is.EqualTo("ssh://grid.example"));
            Assert.That(configuration.WebSocket,   Is.Null);

            var file = """{ "nts": { "groups": { "legal": { "enabled": false } } }, "gridOperator": { "@id": "DE*SWJ", "name": "Netze Beispielstadt" }, "publicURLs": { "ssh": "ssh://root@grid.example" } }""";

            Assert.That(() => GridOperatorFrom(file), Throws.InvalidOperationException.With.Message.Contains("'publicURLs.ssh' names no user"));

        }

        #endregion

    }

}
