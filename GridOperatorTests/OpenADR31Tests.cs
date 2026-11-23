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
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The VTN of a grid operator as OpenADR 3.1 has it: targets as strings,
    /// granted to each VEN and hidden from the others; reports and
    /// subscriptions of a clientID, read only by it; webhooks checked by an
    /// echo, and told only of what their VEN may read; and the notifiers.
    /// </summary>
    public class OpenADR31Tests : AGridOperatorTests
    {

        #region (helper) AGridWithTwoPoints(GridOperator)

        /// <summary>
        /// A grid operator with two grid connection points, and a local
        /// controller behind each.
        /// </summary>
        private static (String ParadiesSecret, String NordSecret) AGridWithTwoPoints(GridOperator GridOperator)
        {

            Assert.That(GridOperator.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(GridOperator.TryAddGridConnectionPoint(APoint("GCP-2"), "test", out _, out error,     out _), Is.True, error);

            Assert.That(GridOperator.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out var paradies, out error, out _), Is.True, error);
            Assert.That(GridOperator.TryAddLocalController(ALocalController("lc-nord",     "DE*GEF*P2", "GCP-2"), "test", out _, out var nord,     out error, out _), Is.True, error);

            return (paradies!, nord!);

        }

        #endregion

        #region (helper) AVENSignedInAs(VENName, Secret)

        /// <summary>
        /// A client of the VTN with the bearer token of the given local controller.
        /// </summary>
        private async Task<HttpClient> AVENSignedInAs(String VENName, String Secret)
        {

            var client  = Anonymous();
            var answer  = await client.PostAsync("openadr3/auth/token", new FormUrlEncodedContent(new Dictionary<String, String> {
                                                                            { "grant_type",     "client_credentials" },
                                                                            { "client_id",      VENName },
                                                                            { "client_secret",  Secret }
                                                                        }));
            var token   = JObject.Parse(await answer.Content.ReadAsStringAsync());

            Assert.That(answer.StatusCode,                  Is.EqualTo(HttpStatusCode.OK), token.ToString());
            Assert.That(token["scope"]?.Value<String>(),    Does.Contain("read_targets").And.Contain("read_ven_objects"));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token["access_token"]!.Value<String>());

            return client;

        }

        #endregion


        #region AVENReadsOnlyWithTheTargetsGrantedToIt()

        /// <summary>
        /// The ven of a local controller carries its clientID and the targets
        /// granted to it; it reads a limit sent to both local controllers with
        /// only its own target, one sent to the other not at all, and none
        /// where it asks for targets not granted to it.
        /// </summary>
        [Test]
        public async Task AVENReadsOnlyWithTheTargetsGrantedToIt()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, _) = AGridWithTwoPoints(grid);

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60 }"""),                                    "test", out var forBoth, out var error, out _), Is.True, error);
            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 11, "durationMinutes": 60, "gridConnectionPointId": "GCP-2" }"""), "test", out var other,   out error,     out _), Is.True, error);

            Assert.That(forBoth!.Event.Targets, Is.EquivalentTo(new[] { "ven:lc-paradies", "ven:lc-nord" }));

            using var vtn = await AVENSignedInAs("lc-paradies", secret);

            var (vensStatus,    vens)     = await Send(vtn, HttpMethod.Get, "openadr3/vens");
            var (eventsStatus,  events)   = await Send(vtn, HttpMethod.Get, "openadr3/events");
            var (otherStatus,   _)        = await Send(vtn, HttpMethod.Get, $"openadr3/events/{other!.Id}");
            var (askedStatus,   asked)    = await Send(vtn, HttpMethod.Get, "openadr3/events?targets=ven:lc-nord");
            var (ownStatus,     own)      = await Send(vtn, HttpMethod.Get, "openadr3/events?targets=ven:lc-paradies&targets=gridConnectionPoint:GCP-1");
            var (resStatus,     res)      = await Send(vtn, HttpMethod.Get, "openadr3/resources");

            Assert.Multiple(() => {

                Assert.That(vensStatus,                                             Is.EqualTo(HttpStatusCode.OK));
                Assert.That(vens[0]?["objectType"]?.Value<String>(),                Is.EqualTo("VEN"));
                Assert.That(vens[0]?["clientID"]?.Value<String>(),                  Is.EqualTo("lc-paradies"));
                Assert.That(vens[0]?["targets"]?.Values<String>(),                  Is.EquivalentTo(new[] { "ven:lc-paradies", "gridConnectionPoint:GCP-1", "chargingPool:DE*GEF*P1" }));

                Assert.That(eventsStatus,                                           Is.EqualTo(HttpStatusCode.OK));
                Assert.That(events.Select(one => one["id"]!.Value<String>()),       Is.EqualTo(new[] { forBoth.Id.ToString() }));
                Assert.That(events[0]?["targets"]?.Values<String>(),                Is.EqualTo(new[] { "ven:lc-paradies" }), "the target of the other local controller is not hidden");

                Assert.That(otherStatus,                                            Is.EqualTo(HttpStatusCode.NotFound));

                Assert.That(askedStatus,                                            Is.EqualTo(HttpStatusCode.OK));
                Assert.That(asked,                                                  Is.Empty, "a VEN reads with targets not granted to it");

                Assert.That(ownStatus,                                              Is.EqualTo(HttpStatusCode.OK));
                Assert.That(own.Select(one => one["id"]!.Value<String>()),          Is.EqualTo(new[] { forBoth.Id.ToString() }));

                Assert.That(resStatus,                                              Is.EqualTo(HttpStatusCode.OK));
                Assert.That(res,                                                    Is.Empty);

            });

        }

        #endregion

        #region AReportIsOfTheClientIDThatSentIt()

        /// <summary>
        /// A report carries the clientID of the VEN that sent it, whatever its
        /// clientName says, and no programID; another VEN neither reads,
        /// changes nor removes it.
        /// </summary>
        [Test]
        public async Task AReportIsOfTheClientIDThatSentIt()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, nordSecret) = AGridWithTwoPoints(grid);

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60 }"""), "test", out var limit, out var error, out _), Is.True, error);

            using var paradies  = await AVENSignedInAs("lc-paradies", secret);
            using var nord      = await AVENSignedInAs("lc-nord",     nordSecret);

            var report = JObject.Parse($$"""
                             { "eventID": "{{limit!.Id}}", "clientName": "somebody else", "clientID": "lc-nord",
                               "resources": [ { "resourceName": "pool", "intervals": [ { "id": 0, "payloads": [ { "type": "USAGE", "values": [ 41.5 ] } ] } ] } ] }
                         """);

            var (postStatus, posted) = await Send(paradies, HttpMethod.Post, "openadr3/reports", report);
            var id                   = posted["id"]?.Value<String>();

            var (nordList,   nordReports)  = await Send(nord,     HttpMethod.Get,    "openadr3/reports");
            var (nordOne,    _)            = await Send(nord,     HttpMethod.Get,    $"openadr3/reports/{id}");
            var (nordDelete, _)            = await Send(nord,     HttpMethod.Delete, $"openadr3/reports/{id}");
            var (nordPut,    _)            = await Send(nord,     HttpMethod.Put,    $"openadr3/reports/{id}", report);
            var (ownPut,     changed)      = await Send(paradies, HttpMethod.Put,    $"openadr3/reports/{id}", report);
            var (ownList,    ownReports)   = await Send(paradies, HttpMethod.Get,    "openadr3/reports");

            Assert.Multiple(() => {

                Assert.That(postStatus,                                  Is.EqualTo(HttpStatusCode.Created), posted.ToString());
                Assert.That(posted["clientID"]?.Value<String>(),         Is.EqualTo("lc-paradies"), "the VTN writes the clientID of who reports");
                Assert.That(posted["clientName"]?.Value<String>(),       Is.EqualTo("somebody else"));
                Assert.That(posted["programID"],                         Is.Null);
                Assert.That(posted["objectType"]?.Value<String>(),       Is.EqualTo("REPORT"));

                Assert.That(nordList,                                    Is.EqualTo(HttpStatusCode.OK));
                Assert.That(nordReports,                                 Is.Empty, "another VEN reads the report");
                Assert.That(nordOne,                                     Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(nordDelete,                                  Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(nordPut,                                     Is.EqualTo(HttpStatusCode.NotFound));

                Assert.That(ownPut,                                      Is.EqualTo(HttpStatusCode.OK), changed.ToString());
                Assert.That(changed["id"]?.Value<String>(),              Is.EqualTo(id));
                Assert.That(ownList,                                     Is.EqualTo(HttpStatusCode.OK));
                Assert.That(ownReports.Count(),                          Is.EqualTo(1));

            });

            var (ownDelete, _) = await Send(paradies, HttpMethod.Delete, $"openadr3/reports/{id}");

            Assert.Multiple(() => {
                Assert.That(ownDelete,                                   Is.EqualTo(HttpStatusCode.OK));
                Assert.That(grid.OpenADR.Reports(),                      Is.Empty);
            });

        }

        #endregion

        #region TheAuthServerAndTheNotifiers()

        /// <summary>
        /// GET auth/server and GET notifiers of 3.1; a bad request at
        /// auth/token is a problem, no longer an error of OAuth; and the MQTT
        /// topics are not there.
        /// </summary>
        [Test]
        public async Task TheAuthServerAndTheNotifiers()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, _) = AGridWithTwoPoints(grid);

            using var anonymous = Anonymous();
            using var vtn       = await AVENSignedInAs("lc-paradies", secret);

            var (serverStatus,    server)     = await Send(anonymous, HttpMethod.Get,  "openadr3/auth/server");
            var (badStatus,       bad)        = await Send(anonymous, HttpMethod.Post, "openadr3/auth/token", new JObject());
            var (notifiersStatus, notifiers)  = await Send(vtn,       HttpMethod.Get,  "openadr3/notifiers");
            var (topicsStatus,    _)          = await Send(vtn,       HttpMethod.Get,  "openadr3/notifiers/mqtt/topics/programs");

            Assert.Multiple(() => {
                Assert.That(serverStatus,                                Is.EqualTo(HttpStatusCode.OK));
                Assert.That(server["tokenURL"]?.Value<String>(),         Does.EndWith("/openadr3/auth/token"));
                Assert.That(badStatus,                                   Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(bad["status"]?.Value<Int32>(),               Is.EqualTo(400), "a problem of RFC 9457");
                Assert.That(bad["error"],                                Is.Null, "an error of OAuth");
                Assert.That(notifiersStatus,                             Is.EqualTo(HttpStatusCode.OK));
                Assert.That(notifiers["WEBHOOK"]?.Value<Boolean>(),      Is.True);
                Assert.That(notifiers["MQTT"],                           Is.Null);
                Assert.That(topicsStatus,                                Is.EqualTo(HttpStatusCode.NotFound));
            });

        }

        #endregion

        #region AWebhookIsCheckedAndToldOnlyWhatItsVENMayRead()

        /// <summary>
        /// A subscription to new events: its callback is checked by an echo
        /// before it is kept, one that does not answer it is refused, and the
        /// webhook is told of a limit for its local controller - with only its
        /// own target - and not of a limit for another.
        /// </summary>
        [Test]
        public async Task AWebhookIsCheckedAndToldOnlyWhatItsVENMayRead()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, _) = AGridWithTwoPoints(grid);

            using var hook  = new Webhook();
            using var vtn   = await AVENSignedInAs("lc-paradies", secret);

            var subscription = JObject.Parse($$"""
                                   { "clientName": "lc-paradies",
                                     "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE", "DELETE" ], "callbackUrl": "{{hook.URL}}good", "bearerToken": "hook-token" } ] }
                               """);

            var (createdStatus, created) = await Send(vtn, HttpMethod.Post, "openadr3/subscriptions", subscription);

            var refusing = (JObject) subscription.DeepClone();
            refusing["objectOperations"]![0]!["callbackUrl"] = hook.URL + "refusing";

            var (refusedStatus, _)       = await Send(vtn, HttpMethod.Post, "openadr3/subscriptions", refusing);

            Assert.Multiple(() => {
                Assert.That(createdStatus,                               Is.EqualTo(HttpStatusCode.Created), created.ToString());
                Assert.That(created["clientID"]?.Value<String>(),        Is.EqualTo("lc-paradies"));
                Assert.That(created["programID"],                        Is.Null);
                Assert.That(hook.Checks,                                 Has.Count.EqualTo(2), "the callbacks were not checked");
                Assert.That(refusedStatus,                               Is.EqualTo(HttpStatusCode.BadRequest), "a callback that does not echo is taken");
                Assert.That(grid.Subscriptions.All(),                    Has.Count.EqualTo(1));
            });

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 11, "durationMinutes": 60, "gridConnectionPointId": "GCP-2" }"""), "test", out _,        out var error, out _), Is.True, error);
            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60 }"""),                                    "test", out var both, out error,     out _), Is.True, error);

            var told = await hook.Notified(TimeSpan.FromSeconds(10));

            Assert.Multiple(() => {
                Assert.That(told.Body["operation"]?.Value<String>(),                     Is.EqualTo("CREATE"));
                Assert.That(told.Body["objectType"]?.Value<String>(),                    Is.EqualTo("EVENT"));
                Assert.That(told.Body["object"]?["id"]?.Value<String>(),                 Is.EqualTo(both!.Id.ToString()), "told of a limit for another local controller");
                Assert.That(told.Body["object"]?["targets"]?.Values<String>(),           Is.EqualTo(new[] { "ven:lc-paradies" }));
                Assert.That(told.Body["targets"]?.Values<String>(),                      Is.EqualTo(new[] { "ven:lc-paradies" }));
                Assert.That(told.Authorization,                                          Is.EqualTo("Bearer hook-token"));
            });

            var id = created["id"]!.Value<String>();

            var (deletedStatus, _) = await Send(vtn, HttpMethod.Delete, $"openadr3/subscriptions/{id}");

            Assert.Multiple(() => {
                Assert.That(deletedStatus,                    Is.EqualTo(HttpStatusCode.OK));
                Assert.That(grid.Subscriptions.All(),         Is.Empty);
            });

        }

        #endregion


        #region TheProgramIsEverybodysAndTheVENRecordOnlyItsOwn()

        /// <summary>
        /// The program has no targets: every VEN reads it, whatever targets it
        /// asks with. The ven records: only its own, by id, by name and by
        /// the targets granted to it.
        /// </summary>
        [Test]
        public async Task TheProgramIsEverybodysAndTheVENRecordOnlyItsOwn()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, nordSecret) = AGridWithTwoPoints(grid);

            using var paradies  = await AVENSignedInAs("lc-paradies", secret);
            using var nord      = await AVENSignedInAs("lc-nord",     nordSecret);

            var nordVEN = grid.OpenADR.Login("lc-nord")!.VENId;

            var (_, programsOfParadies)  = await Send(paradies, HttpMethod.Get, "openadr3/programs");
            var (_, programsOfNord)      = await Send(nord,     HttpMethod.Get, "openadr3/programs?targets=ven:lc-nord");
            var (_, byName)              = await Send(paradies, HttpMethod.Get, $"openadr3/programs?programName={GridOperator.ProgramName}");
            var (_, byOtherName)         = await Send(paradies, HttpMethod.Get, "openadr3/programs?programName=another");

            var (otherVEN,   _)          = await Send(paradies, HttpMethod.Get, $"openadr3/vens/{nordVEN}");
            var (_, byVenName)           = await Send(paradies, HttpMethod.Get, "openadr3/vens?venName=lc-nord");
            var (_, byOwnTarget)         = await Send(paradies, HttpMethod.Get, "openadr3/vens?targets=gridConnectionPoint:GCP-1");
            var (_, byOtherTarget)       = await Send(paradies, HttpMethod.Get, "openadr3/vens?targets=gridConnectionPoint:GCP-2");
            var (ownVEN,     own)        = await Send(nord,     HttpMethod.Get, $"openadr3/vens/{nordVEN}");

            Assert.Multiple(() => {

                Assert.That(programsOfParadies.Count(),                                 Is.EqualTo(1));
                Assert.That(programsOfNord.Count(),                                     Is.EqualTo(1), "a program without targets is not everybody's");
                Assert.That(programsOfParadies[0]?["attributes"]?.Select(attribute => attribute["type"]?.Value<String>()),
                                                                                        Does.Contain("RETAILER_NAME").And.Contain("PROGRAM_LONG_NAME"));
                Assert.That(programsOfParadies[0]?["retailerName"],                     Is.Null, "a field of 3.0.1");
                Assert.That(byName.Count(),                                             Is.EqualTo(1));
                Assert.That(byOtherName,                                                Is.Empty);

                Assert.That(otherVEN,                                                   Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(byVenName,                                                  Is.Empty);
                Assert.That(byOwnTarget.Count(),                                        Is.EqualTo(1));
                Assert.That(byOtherTarget,                                              Is.Empty);
                Assert.That(ownVEN,                                                     Is.EqualTo(HttpStatusCode.OK));
                Assert.That(own["clientID"]?.Value<String>(),                           Is.EqualTo("lc-nord"));

            });

        }

        #endregion

        #region WhatOnlyTheGridOperatorWrites()

        /// <summary>
        /// Programs, events, vens and resources are the grid operator's; a
        /// resource it has none of; a bad target is a bad request; and a token
        /// for credentials no local controller has is a problem of 401.
        /// </summary>
        [Test]
        public async Task WhatOnlyTheGridOperatorWrites()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, _) = AGridWithTwoPoints(grid);

            using var vtn        = await AVENSignedInAs("lc-paradies", secret);
            using var anonymous  = Anonymous();

            var (programs,   _)        = await Send(vtn, HttpMethod.Post,   "openadr3/programs",  new JObject(new JProperty("programName", "mine")));
            var (events,     _)        = await Send(vtn, HttpMethod.Post,   "openadr3/events",    new JObject(new JProperty("programID",   GridOperator.ProgramName)));
            var (vens,       _)        = await Send(vtn, HttpMethod.Post,   "openadr3/vens",      JObject.Parse("""{ "objectType": "VEN_VEN_REQUEST", "venName": "v" }"""));
            var (resources,  _)        = await Send(vtn, HttpMethod.Post,   "openadr3/resources", JObject.Parse("""{ "objectType": "VEN_RESOURCE_REQUEST", "resourceName": "r", "venID": "0" }"""));
            var (deleteVEN,  _)        = await Send(vtn, HttpMethod.Delete, $"openadr3/vens/{grid.OpenADR.Login("lc-paradies")!.VENId}");
            var (resource,   _)        = await Send(vtn, HttpMethod.Get,    "openadr3/resources/0");
            var (badTarget,  bad)      = await Send(vtn, HttpMethod.Get,    $"openadr3/events?targets={new String('t', 129)}");
            var (unknown,    problem)  = await Send(anonymous, HttpMethod.Post, "openadr3/auth/token", new JObject(new JProperty("clientID", "nobody"), new JProperty("clientSecret", "nothing")));
            var (noToken,    _)        = await Send(anonymous, HttpMethod.Get,  "openadr3/subscriptions");

            Assert.Multiple(() => {
                Assert.That(programs,                           Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(events,                             Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(vens,                               Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(resources,                          Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(deleteVEN,                          Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(resource,                           Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(badTarget,                          Is.EqualTo(HttpStatusCode.BadRequest), bad.ToString());
                Assert.That(unknown,                            Is.EqualTo(HttpStatusCode.Unauthorized));
                Assert.That(problem["status"]?.Value<Int32>(),  Is.EqualTo(401), "a problem of RFC 9457");
                Assert.That(noToken,                            Is.EqualTo(HttpStatusCode.Unauthorized));
            });

        }

        #endregion

        #region ActiveLeavesOutWhatIsOver()

        /// <summary>
        /// "?active=true" leaves out a limit that is over; without it, it is
        /// listed, too.
        /// </summary>
        [Test]
        public async Task ActiveLeavesOutWhatIsOver()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, _) = AGridWithTwoPoints(grid);
            var now         = DateTimeOffset.UtcNow;

            var over = OpenADR.GridEvent.New(grid.Program.Id!.Value,
                                             "IMPORT_CAPACITY_LIMIT",
                                             10,
                                             now - TimeSpan.FromHours(3),
                                             TimeSpan.FromHours(1),
                                             [ "lc-paradies" ],
                                             "GCP-1",
                                             "over",
                                             null,
                                             "test",
                                             now - TimeSpan.FromHours(3));

            Assert.That(grid.OpenADR.TryAddEvent(over, out var error), Is.True, error);
            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var current, out error, out _), Is.True, error);

            using var vtn = await AVENSignedInAs("lc-paradies", secret);

            var (_, all)     = await Send(vtn, HttpMethod.Get, "openadr3/events");
            var (_, active)  = await Send(vtn, HttpMethod.Get, "openadr3/events?active=true");

            Assert.Multiple(() => {
                Assert.That(all.   Select(one => one["id"]!.Value<String>()),  Is.EquivalentTo(new[] { over.Id.ToString(), current!.Id.ToString() }));
                Assert.That(active.Select(one => one["id"]!.Value<String>()),  Is.EqualTo(new[] { current.Id.ToString() }));
            });

        }

        #endregion

        #region ASubscriptionIsOfTheClientIDThatMadeIt()

        /// <summary>
        /// A subscription is read, changed and removed only by the VEN that
        /// made it; another neither lists it nor reaches it by its id. A
        /// change keeps its id and its clientID.
        /// </summary>
        [Test]
        public async Task ASubscriptionIsOfTheClientIDThatMadeIt()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, nordSecret) = AGridWithTwoPoints(grid);

            using var hook      = new Webhook();
            using var paradies  = await AVENSignedInAs("lc-paradies", secret);
            using var nord      = await AVENSignedInAs("lc-nord",     nordSecret);

            var subscription = JObject.Parse($$"""
                                   { "clientName": "paradies", "clientID": "lc-nord",
                                     "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE" ], "callbackUrl": "{{hook.URL}}good" } ] }
                               """);

            var (_, created)          = await Send(paradies, HttpMethod.Post, "openadr3/subscriptions", subscription);
            var id                    = created["id"]!.Value<String>();

            var changed               = (JObject) subscription.DeepClone();
            changed["clientName"]     = "paradies, changed";

            var (nordList,   listed)  = await Send(nord,     HttpMethod.Get,    "openadr3/subscriptions");
            var (nordOne,    _)       = await Send(nord,     HttpMethod.Get,    $"openadr3/subscriptions/{id}");
            var (nordPut,    _)       = await Send(nord,     HttpMethod.Put,    $"openadr3/subscriptions/{id}", changed);
            var (nordDelete, _)       = await Send(nord,     HttpMethod.Delete, $"openadr3/subscriptions/{id}");
            var (ownPut,     put)     = await Send(paradies, HttpMethod.Put,    $"openadr3/subscriptions/{id}", changed);
            var (ownOne,     one)     = await Send(paradies, HttpMethod.Get,    $"openadr3/subscriptions/{id}");
            var (_,          byObj)   = await Send(paradies, HttpMethod.Get,    "openadr3/subscriptions?objects=REPORT");

            Assert.Multiple(() => {

                Assert.That(created["clientID"]?.Value<String>(),     Is.EqualTo("lc-paradies"), "the clientID of the body is taken");

                Assert.That(nordList,                                 Is.EqualTo(HttpStatusCode.OK));
                Assert.That(listed,                                   Is.Empty);
                Assert.That(nordOne,                                  Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(nordPut,                                  Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(nordDelete,                               Is.EqualTo(HttpStatusCode.NotFound));

                Assert.That(ownPut,                                   Is.EqualTo(HttpStatusCode.OK), put.ToString());
                Assert.That(put["id"]?.Value<String>(),               Is.EqualTo(id));
                Assert.That(put["clientID"]?.Value<String>(),         Is.EqualTo("lc-paradies"));
                Assert.That(ownOne,                                   Is.EqualTo(HttpStatusCode.OK));
                Assert.That(one["clientName"]?.Value<String>(),       Is.EqualTo("paradies, changed"));
                Assert.That(byObj,                                    Is.Empty, "a subscription to events is listed for reports");
                Assert.That(grid.Subscriptions.Of("lc-paradies"),     Has.Count.EqualTo(1));

            });

        }

        #endregion

        #region AWrongEchoIsNoSubscription()

        /// <summary>
        /// A callback that answers its check with 200, but not with the echo
        /// it was sent, is no subscription; nor is a callback that is no URL
        /// of HTTP.
        /// </summary>
        [Test]
        public async Task AWrongEchoIsNoSubscription()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, _) = AGridWithTwoPoints(grid);

            using var hook  = new Webhook();
            using var vtn   = await AVENSignedInAs("lc-paradies", secret);

            JObject ASubscription(String Callback)
                => JObject.Parse($$"""
                       { "clientName": "p", "objectOperations": [ { "objects": [ "EVENT" ], "operations": [ "CREATE" ], "callbackUrl": "{{Callback}}" } ] }
                   """);

            var (wrong,   _)  = await Send(vtn, HttpMethod.Post, "openadr3/subscriptions", ASubscription(hook.URL + "wrong"));
            var (nobody,  _)  = await Send(vtn, HttpMethod.Post, "openadr3/subscriptions", ASubscription("http://127.0.0.1:1/nobody"));
            var (ftp,     _)  = await Send(vtn, HttpMethod.Post, "openadr3/subscriptions", ASubscription("ftp://127.0.0.1/hook"));

            Assert.Multiple(() => {
                Assert.That(wrong,                        Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(nobody,                       Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(ftp,                          Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(grid.Subscriptions.All(),     Is.Empty);
            });

        }

        #endregion

        #region ACallbackIsHTTPS()

        /// <summary>
        /// A callback of plain HTTP is taken only where it is allowed - from a
        /// VEN that came over plain HTTP.
        /// </summary>
        [Test]
        public void ACallbackIsHTTPS()
        {

            Assert.Multiple(() => {
                Assert.That(OpenADR.WebhookNotifier.IsAcceptableCallback("https://ven.example.org/hook", AllowHTTP: false, out _), Is.True);
                Assert.That(OpenADR.WebhookNotifier.IsAcceptableCallback("http://ven.example.org/hook",  AllowHTTP: false, out _), Is.False);
                Assert.That(OpenADR.WebhookNotifier.IsAcceptableCallback("http://ven.example.org/hook",  AllowHTTP: true,  out _), Is.True);
                Assert.That(OpenADR.WebhookNotifier.IsAcceptableCallback("/hook",                        AllowHTTP: true,  out _), Is.False);
                Assert.That(OpenADR.WebhookNotifier.IsAcceptableCallback("wss://ven.example.org/hook",   AllowHTTP: true,  out _), Is.False);
            });

        }

        #endregion

        #region EachSubscriptionIsToldOnlyWhatItAskedForAndMayRead()

        /// <summary>
        /// Two VENs, each with a subscription to events and to reports: a
        /// limit for one point is told only to the VEN behind it, its
        /// cancellation, too; the reports of a VEN - created, changed,
        /// removed - only to that VEN.
        /// </summary>
        [Test]
        public async Task EachSubscriptionIsToldOnlyWhatItAskedForAndMayRead()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, nordSecret) = AGridWithTwoPoints(grid);

            using var paradiesHook  = new Webhook();
            using var nordHook      = new Webhook();
            using var paradies      = await AVENSignedInAs("lc-paradies", secret);
            using var nord          = await AVENSignedInAs("lc-nord",     nordSecret);

            JObject ASubscription(Webhook Hook)
                => JObject.Parse($$"""
                       { "clientName": "c", "objectOperations": [
                           { "objects": [ "EVENT", "REPORT" ], "operations": [ "CREATE", "UPDATE", "DELETE" ], "callbackUrl": "{{Hook.URL}}good" } ] }
                   """);

            Assert.That((await Send(paradies, HttpMethod.Post, "openadr3/subscriptions", ASubscription(paradiesHook))).Status, Is.EqualTo(HttpStatusCode.Created));
            Assert.That((await Send(nord,     HttpMethod.Post, "openadr3/subscriptions", ASubscription(nordHook))).    Status, Is.EqualTo(HttpStatusCode.Created));

            #region A limit for the first point, and its cancellation

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var limit, out var error, out _), Is.True, error);

            var created = await paradiesHook.Notified(TimeSpan.FromSeconds(10));

            Assert.That(grid.TryCancelLimit(limit!.Id.ToString(), "test", out error, out _), Is.True, error);

            var deleted = await paradiesHook.Notified(TimeSpan.FromSeconds(10));

            Assert.Multiple(() => {
                Assert.That(created.Body["operation"]?.Value<String>(),        Is.EqualTo("CREATE"));
                Assert.That(created.Body["object"]?["id"]?.Value<String>(),    Is.EqualTo(limit.Id.ToString()));
                Assert.That(deleted.Body["operation"]?.Value<String>(),        Is.EqualTo("DELETE"));
                Assert.That(deleted.Body["object"]?["id"]?.Value<String>(),    Is.EqualTo(limit.Id.ToString()));
            });

            #endregion

            #region A report of the first VEN: created, changed, removed

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 22, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var second, out error, out _), Is.True, error);
            Assert.That((await paradiesHook.Notified(TimeSpan.FromSeconds(10))).Body["operation"]?.Value<String>(), Is.EqualTo("CREATE"));

            var report = JObject.Parse($$"""{ "eventID": "{{second!.Id}}", "clientName": "c", "resources": [] }""");

            var (_, posted) = await Send(paradies, HttpMethod.Post, "openadr3/reports", report);
            var reportId    = posted["id"]!.Value<String>();

            var reportCreated = await paradiesHook.Notified(TimeSpan.FromSeconds(10));

            report["reportName"] = "changed";
            await Send(paradies, HttpMethod.Put, $"openadr3/reports/{reportId}", report);

            var reportUpdated = await paradiesHook.Notified(TimeSpan.FromSeconds(10));

            await Send(paradies, HttpMethod.Delete, $"openadr3/reports/{reportId}");

            var reportDeleted = await paradiesHook.Notified(TimeSpan.FromSeconds(10));

            Assert.Multiple(() => {
                Assert.That(reportCreated.Body["objectType"]?.Value<String>(),             Is.EqualTo("REPORT"));
                Assert.That(reportCreated.Body["operation"]?.Value<String>(),              Is.EqualTo("CREATE"));
                Assert.That(reportCreated.Body["object"]?["clientID"]?.Value<String>(),    Is.EqualTo("lc-paradies"));
                Assert.That(reportUpdated.Body["operation"]?.Value<String>(),              Is.EqualTo("UPDATE"));
                Assert.That(reportUpdated.Body["object"]?["reportName"]?.Value<String>(),  Is.EqualTo("changed"));
                Assert.That(reportDeleted.Body["operation"]?.Value<String>(),              Is.EqualTo("DELETE"));
            });

            #endregion

            Assert.That(nordHook.NothingWithin(TimeSpan.FromSeconds(2)), Is.True, "The other VEN was told of a limit or a report not its own.");

        }

        #endregion


        #region (class) Webhook

        /// <summary>
        /// A webhook of a VEN on the loopback: it answers a check of "/good"
        /// with its echo, one of "/wrong" with 200 and something else, one of
        /// "/refusing" with 403, and keeps every notification posted to it.
        /// </summary>
        private sealed class Webhook : IDisposable
        {

            private readonly TcpListener                                             listener;
            private readonly CancellationTokenSource                                 stop          = new ();
            private readonly BlockingCollection<(JObject Body, String? Authorization)>  notifications = [];

            public String               URL     { get; }
            public ConcurrentBag<String> Checks  { get; } = [];

            public Webhook()
            {

                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();

                URL = $"http://127.0.0.1:{((IPEndPoint) listener.LocalEndpoint).Port}/";

                _ = Task.Run(async () => {
                    while (!stop.IsCancellationRequested)
                    {
                        TcpClient client;
                        try
                        {
                            client = await listener.AcceptTcpClientAsync(stop.Token);
                        }
                        catch (Exception)
                        {
                            return;
                        }
                        _ = Task.Run(() => Serve(client));
                    }
                });

            }

            public Boolean NothingWithin(TimeSpan Timeout)
                => !notifications.TryTake(out _, Timeout);

            public Task<(JObject Body, String? Authorization)> Notified(TimeSpan Timeout)

                => Task.Run(() => notifications.TryTake(out var notification, Timeout)
                                      ? notification
                                      : throw new TimeoutException("The webhook was told nothing."));

            private async Task Serve(TcpClient Client)
            {
                using (Client)
                {

                    var stream  = Client.GetStream();
                    var head    = new StringBuilder();
                    var buffer  = new Byte[1];

                    while (!head.ToString().EndsWith("\r\n\r\n") && await stream.ReadAsync(buffer) == 1)
                        head.Append((Char) buffer[0]);

                    var lines          = head.ToString().Split("\r\n");
                    var requestLine    = lines[0].Split(' ');
                    var length         = lines.Where (line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)).
                                               Select(line => Int32.Parse(line[15..].Trim())).
                                               FirstOrDefault();
                    var authorization  = lines.Where (line => line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)).
                                               Select(line => line[14..].Trim()).
                                               FirstOrDefault();

                    var body = new Byte[length];
                    var read = 0;

                    while (read < length)
                        read += await stream.ReadAsync(body.AsMemory(read));

                    var (status, answer) = (200, "");

                    if (requestLine[0] == "GET")
                    {
                        Checks.Add(requestLine[1]);
                        var echo = requestLine[1].Split("echo=").ElementAtOrDefault(1) ?? "";
                        (status, answer) = requestLine[1].StartsWith("/good")  ? (200, echo)
                                         : requestLine[1].StartsWith("/wrong") ? (200, "not the echo")
                                         :                                       (403, "");
                    }
                    else
                        notifications.Add((JObject.Parse(Encoding.UTF8.GetString(body)), authorization));

                    var bytes = Encoding.UTF8.GetBytes(answer);

                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status} X\r\nContent-Length: {bytes.Length}\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\n"));
                    await stream.WriteAsync(bytes);

                }
            }

            public void Dispose()
            {
                stop.Cancel();
                listener.Stop();
            }

        }

        #endregion

    }

}
