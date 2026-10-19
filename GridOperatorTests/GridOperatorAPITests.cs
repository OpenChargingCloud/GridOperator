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

            using var planner     = await SignedInAs("planner1",    "planner");
            using var dispatcher  = await SignedInAs("dispatcher1", "dispatcher");
            using var viewer      = await SignedInAs("viewer1",     "viewer");
            using var admin       = await SignedInAs("admin1",      "systemadmin");

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

    }

}
