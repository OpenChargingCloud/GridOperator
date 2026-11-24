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

using org.GraphDefined.Vanaheimr.Illias;
using System.Net;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OpenADRv3;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The JSON API of a grid operator, with its roles: a planner keeps the
    /// grid connection points and sends nothing; a dispatcher sends a limit
    /// and changes no point; a viewer looks; an administrator adds the local
    /// controllers.
    /// </summary>
    public class GridOperatorAPITests : AGridOperatorTests
    {

        #region EveryRoleDoesWhatItMay()

        [Test]
        public async Task EveryRoleDoesWhatItMay()
        {

            await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            using var planner     = await SignedInAs(User_Id.Parse("planner1"),    "planner");
            using var dispatcher  = await SignedInAs(User_Id.Parse("dispatcher1"), "dispatcher");
            using var viewer      = await SignedInAs(User_Id.Parse("viewer1"),     "viewer");
            using var admin       = await SignedInAs(User_Id.Parse("admin1"),      "systemadmin");

            #region The grid operator and its points

            var (status, json) = await Send(viewer, HttpMethod.Get, "api/v1/gridOperator");
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), json.ToString());
            Assert.That(json["gridOperator"]?["@id"]?.Value<String>(),          Is.EqualTo("DE*SWJ"));
            Assert.That(json["party"]?["signingKeys"]?.Count(),                 Is.EqualTo(1));

            (status, json) = await Send(planner,    HttpMethod.Put,  "api/v1/gridOperator", JObject.Parse("""{ "hotline": "+49 3641 7654321" }"""));
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), json.ToString());

            (status, _)    = await Send(dispatcher, HttpMethod.Post, "api/v1/gridConnectionPoints", APoint("GCP-1"));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), "a dispatcher added a grid connection point");

            (status, json) = await Send(planner,    HttpMethod.Post, "api/v1/gridConnectionPoints", APoint("GCP 1"));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), json.ToString());

            (status, json) = await Send(viewer,     HttpMethod.Get,  "api/v1/gridConnectionPoints/GCP%201");
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), json.ToString());
            Assert.That(json["contractedImportPower"]?.Value<Int32>(),          Is.EqualTo(110000));

            var changed = APoint("GCP 1", 200000);
            (status, json) = await Send(planner,    HttpMethod.Put,  "api/v1/gridConnectionPoints/GCP%201", changed);
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), json.ToString());
            Assert.That(json["contractedImportPower"]?.Value<Int32>(),          Is.EqualTo(200000));

            #endregion

            #region The local controller and a limit for it

            (status, _)    = await Send(planner,    HttpMethod.Post, "api/v1/openADR/localControllers", ALocalController("lc-1", "DE*GEF*P1", "GCP 1"));
            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), "a planner let a local controller sign in");

            (status, json) = await Send(admin,      HttpMethod.Post, "api/v1/openADR/localControllers", ALocalController("lc-1", "DE*GEF*P1", "GCP 1"));
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.Created), json.ToString());
            Assert.That(json["credentials"]?["clientSecret"]?.Value<String>(),  Is.Not.Empty);

            (status, json) = await Send(viewer,     HttpMethod.Get,  "api/v1/openADR/localControllers");
            Assert.That(json.ToString(), Does.Not.Contain("clientSecret").And.Not.Contain("sha256"), "a list of local controllers says their secrets");

            var limit = JObject.Parse("""{ "kind": "IMPORT_CAPACITY_LIMIT", "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP 1" }""");

            (status, _)    = await Send(planner,    HttpMethod.Post, "api/v1/openADR/limits", limit);
            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), "a planner sent a limit");

            (status, json) = await Send(dispatcher, HttpMethod.Post, "api/v1/openADR/limits", limit);
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.Created), json.ToString());
            Assert.That(json["localControllers"]?.Values<String>(),             Is.EqualTo(new[] { "lc-1" }));

            var limitId = json["id"]!.Value<String>();

            (status, json) = await Send(viewer,     HttpMethod.Get,  "api/v1/gridConnectionPoints/GCP%201");
            Assert.That(json["localControllers"]?.Values<String>(),             Is.EqualTo(new[] { "lc-1" }));
            Assert.That(json["limits"]?.Count(),                                Is.EqualTo(1));

            (status, _)    = await Send(planner,    HttpMethod.Delete, "api/v1/gridConnectionPoints/GCP%201");
            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict), "a point with a local controller behind it was removed");

            (status, json) = await Send(dispatcher, HttpMethod.Delete, $"api/v1/openADR/limits/{limitId}");
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), json.ToString());
            Assert.That(json["limits"]?.Count(),                                Is.EqualTo(0));

            #endregion

            #region No roaming network yet

            (status, json) = await Send(planner,    HttpMethod.Post, "api/v1/roamingNetwork/publication");
            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict), json.ToString());

            (status, json) = await Send(planner,    HttpMethod.Put,  "api/v1/configuration/roamingNetwork", JObject.Parse("""{ "url": "ftp://example.org/x" }"""));
            Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest), json.ToString());

            (status, json) = await Send(planner,    HttpMethod.Put,  "api/v1/configuration/roamingNetwork", JObject.Parse("""{ "url": "http://127.0.0.1:1/poi/RNs/QA/" }"""));
            Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), json.ToString());
            Assert.That(json["url"]?.Value<String>(),                           Is.EqualTo("http://127.0.0.1:1/poi/RNs/QA"));

            (status, json) = await Send(planner,    HttpMethod.Get,  "api/v1/roamingNetwork/publication");
            Assert.That(status, Is.EqualTo(HttpStatusCode.BadGateway), json.ToString());

            #endregion

        }

        #endregion

        #region ALocalControllerSaysItsConnectionsAndSubscriptions()

        /// <summary>
        /// A local controller in the list says the connections its VEN connects
        /// over - who signs in, over what, in which binding, switched on or off,
        /// and how many notifications wait - and its subscriptions, each callback
        /// a connection or a webhook; nothing of another VEN, and no bearer token.
        /// </summary>
        [Test]
        public async Task ALocalControllerSaysItsConnectionsAndSubscriptions()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            using var viewer = await SignedInAs(User_Id.Parse("viewer1"), "viewer");

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(grid.TryAddLocalController(ALocalController("lc-1", "DE*GEF*P1", "GCP-1"), "test", out var login, out _, out error, out _), Is.True, error);
            Assert.That(grid.TryAddLocalController(ALocalController("lc-2", "DE*GEF*P2", "GCP-1"), "test", out _,         out _, out error, out _), Is.True, error);

            String AConnection(String URLText, String Binding, Boolean Enabled, String VEN)
            {

                Assert.That(grid.Connections.TryAddConnection(I18NString.Create($"The local controller of '{VEN}'"),
                                                              URL.Parse(URLText),
                                                              "VEN",
                                                              Enabled,
                                                              null,
                                                              null,
                                                              out var id,
                                                              out var error,
                                                              Inbound:         true,
                                                              OpenADRBinding:  Binding,
                                                              VEN:             VEN),
                            Is.True, error);

                return id!;

            }

            var messages  = AConnection("ws://127.0.0.1:1/lc-1-messages",  "messages", true,  "lc-1");
            var rest      = AConnection("ssh://lc-1-rest@127.0.0.1:22",     "rest",     false, "lc-1");
            var other     = AConnection("ws://127.0.0.1:1/lc-2",            "messages", true,  "lc-2");

            var now = DateTimeOffset.UtcNow;

            var overConnection  = new Subscription("lc-1 over its connection",
                                                   [ new ObjectOperation([ ObjectType.SUBSCRIPTION ], [ Operation.CREATE ], URL.Parse("ws://127.0.0.1:1/lc-1-messages")) ],
                                                   ClientID:  login!.ClientID,
                                                   Id:        Subscription_Id.NewRandom,
                                                   Created:   now);

            var toAWebhook      = new Subscription("lc-1 to a webhook",
                                                   [ new ObjectOperation([ ObjectType.EVENT ], [ Operation.CREATE, Operation.UPDATE ], URL.Parse("https://lc-1.example.org/hook"), "a-bearer-token-never-shown") ],
                                                   ClientID:  login.ClientID,
                                                   Id:        Subscription_Id.NewRandom,
                                                   Created:   now.AddSeconds(1));

            Assert.That(grid.Subscriptions.TryAdd(overConnection, out error), Is.True, error);
            Assert.That(grid.Subscriptions.TryAdd(toAWebhook,     out error), Is.True, error);

            grid.Subscriptions.DeliverOverConnection(overConnection.Id!.ToString()!, messages);

            // Its VEN is not in: what the subscription over the connection is
            // told of waits for it.
            grid.NotifyOf(ObjectType.SUBSCRIPTION, Operation.CREATE, toAWebhook, login.ClientID);

            Assert.That(grid.NotificationsWaitingFor(messages), Is.EqualTo(1));

            var (status, json) = await Send(viewer, HttpMethod.Get, "api/v1/openADR/localControllers");
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), json.ToString());
            Assert.That(json.ToString(), Does.Not.Contain("a-bearer-token-never-shown"), "a list of local controllers says a bearer token of a subscription");

            var lc1          = json["localControllers"]!.Single(controller => controller["venName"]?.Value<String>() == "lc-1");
            var lc2          = json["localControllers"]!.Single(controller => controller["venName"]?.Value<String>() == "lc-2");
            var connections  = lc1["connections"]!;
            var byMessages   = connections.Single(connection => connection["id"]?.Value<String>() == messages);
            var overREST     = connections.Single(connection => connection["id"]?.Value<String>() == rest);

            Assert.Multiple(() => {

                Assert.That(connections.Count(),                                            Is.EqualTo(2));

                Assert.That(byMessages["login"]?.Value<String>(),                           Is.EqualTo("lc-1-messages"));
                Assert.That(byMessages["transport"]?.Value<String>(),                       Is.EqualTo("ws"));
                Assert.That(byMessages["binding"]?.Value<String>(),                         Is.EqualTo("messages"));
                Assert.That(byMessages["enabled"]?.Value<Boolean>(),                        Is.True);
                Assert.That(byMessages["in"]?.Value<Boolean>(),                             Is.False);
                Assert.That(byMessages["notificationsWaiting"]?.Value<Int32>(),             Is.EqualTo(1));

                Assert.That(overREST["login"]?.Value<String>(),                             Is.EqualTo("lc-1-rest"));
                Assert.That(overREST["transport"]?.Value<String>(),                         Is.EqualTo("ssh"));
                Assert.That(overREST["binding"]?.Value<String>(),                           Is.EqualTo("rest"));
                Assert.That(overREST["enabled"]?.Value<Boolean>(),                          Is.False);
                Assert.That(overREST["in"]?.Value<Boolean>(),                               Is.False);
                Assert.That(overREST["notificationsWaiting"]?.Value<Int32>(),               Is.EqualTo(0));

                Assert.That(lc1["subscriptions"]?.Count(),                                  Is.EqualTo(2));
                Assert.That(lc1["subscriptions"]?[0]?["id"]?.Value<String>(),               Is.EqualTo(overConnection.Id.ToString()));
                Assert.That(lc1["subscriptions"]?[0]?["clientName"]?.Value<String>(),       Is.EqualTo("lc-1 over its connection"));
                Assert.That(lc1["subscriptions"]?[0]?["toldOver"]?.Value<String>(),         Is.EqualTo(messages));
                Assert.That(lc1["subscriptions"]?[0]?["objectOperations"]?[0]?["objects"]?.Values<String>(),     Is.EqualTo(new[] { "SUBSCRIPTION" }));
                Assert.That(lc1["subscriptions"]?[0]?["objectOperations"]?[0]?["callbackUrl"]?.Value<String>(),  Does.StartWith("ws://127.0.0.1:1/lc-1-messages"));
                Assert.That(lc1["subscriptions"]?[0]?["objectOperations"]?[0]?["callback"]?.Value<String>(),     Is.EqualTo("connection"));
                Assert.That(lc1["subscriptions"]?[1]?["toldOver"]?.Type,                    Is.EqualTo(JTokenType.Null));
                Assert.That(lc1["subscriptions"]?[1]?["objectOperations"]?[0]?["operations"]?.Values<String>(),  Is.EqualTo(new[] { "CREATE", "UPDATE" }));
                Assert.That(lc1["subscriptions"]?[1]?["objectOperations"]?[0]?["callback"]?.Value<String>(),     Is.EqualTo("webhook"));

                Assert.That(lc2["connections"]?.Select(connection => connection["id"]?.Value<String>()),          Is.EqualTo(new[] { other }));
                Assert.That(lc2["subscriptions"]?.Count(),                                  Is.EqualTo(0));

            });

        }

        #endregion

    }

}
