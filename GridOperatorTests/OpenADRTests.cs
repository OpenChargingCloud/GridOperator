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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// The OpenADR 3 VTN of a grid operator: the local controllers that may
    /// sign in, the tokens they get, the limits they read - only theirs - and
    /// the reports they send back.
    /// </summary>
    public class OpenADRTests : AGridOperatorTests
    {

        #region (helper) AGridWithTwoPoints()

        /// <summary>
        /// A grid operator with two grid connection points, and a local
        /// controller behind each.
        /// </summary>
        private (String ParadiesSecret, String NordSecret) AGridWithTwoPoints(GridOperator GridOperator)
        {

            Assert.That(GridOperator.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);
            Assert.That(GridOperator.TryAddGridConnectionPoint(APoint("GCP-2"), "test", out _, out error,     out _), Is.True, error);

            Assert.That(GridOperator.TryAddLocalController(ALocalController("lc-paradies", "DE*GEF*P1", "GCP-1"), "test", out _, out var paradies, out error, out _), Is.True, error);
            Assert.That(GridOperator.TryAddLocalController(ALocalController("lc-nord",     "DE*GEF*P2", "gcp-2"), "test", out _, out var nord,     out error, out _), Is.True, error);

            return (paradies!, nord!);

        }

        #endregion


        #region ALimitIsForTheLocalControllersBehindItsPoint()

        /// <summary>
        /// A limit sent to a grid connection point is for the local controllers
        /// behind it, and for nobody else; one for all is for all of them.
        /// </summary>
        [Test]
        public void ALimitIsForTheLocalControllersBehindItsPoint()
        {

            var grid = GridOperatorFrom();

            AGridWithTwoPoints(grid);

            Assert.That(grid.TrySendLimit(new JObject(new JProperty("kind",                   "IMPORT_CAPACITY_LIMIT"),
                                                      new JProperty("limitKW",                44),
                                                      new JProperty("durationMinutes",        60),
                                                      new JProperty("gridConnectionPointId",  "GCP-1")),
                                          "test", out var limit, out var error, out _), Is.True, error);

            Assert.Multiple(() => {
                Assert.That(limit!.VENNames,               Is.EqualTo(new[] { "lc-paradies" }));
                Assert.That(limit.GridConnectionPointId,   Is.EqualTo("GCP-1"));
                Assert.That(limit.Kind,                    Is.EqualTo("IMPORT_CAPACITY_LIMIT"));
                Assert.That(limit.LimitKW,                 Is.EqualTo(44));
                Assert.That(limit.End - limit.Start,       Is.EqualTo(TimeSpan.FromHours(1)));
                Assert.That(limit.IsFor("lc-nord"),        Is.False);
                Assert.That(limit.EventJSON()["programID"]?.Value<String>(), Is.EqualTo(GridOperator.ProgramName));
            });

            Assert.That(grid.TrySendLimit(new JObject(new JProperty("kind",             "EXPORT_CAPACITY_LIMIT"),
                                                      new JProperty("limitKW",          0),
                                                      new JProperty("durationMinutes",  15)),
                                          "test", out var forAll, out error, out _), Is.True, error);

            Assert.That(forAll!.VENNames.Order(), Is.EqualTo(new[] { "lc-nord", "lc-paradies" }));

        }

        #endregion

        #region WhatIsRefused()

        /// <summary>
        /// A limit of another kind, without a duration, for a point that is not
        /// there or with nobody behind it; a local controller of a point that
        /// is not there, or one twice; a point removed with somebody behind it.
        /// </summary>
        [Test]
        public void WhatIsRefused()
        {

            var grid = GridOperatorFrom();

            AGridWithTwoPoints(grid);
            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-3"), "test", out _, out var error, out _), Is.True, error);

            Assert.Multiple(() => {

                Assert.That(grid.TrySendLimit(JObject.Parse("""{ "kind": "PRICE", "limitKW": 1, "durationMinutes": 5 }"""),                         "test", out _, out _, out var refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

                Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 1 }"""),                                                                "test", out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

                Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 1, "durationMinutes": 5, "gridConnectionPointId": "nowhere" }"""),     "test", out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.NotFound));

                Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 1, "durationMinutes": 5, "gridConnectionPointId": "GCP-3" }"""),       "test", out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Conflict));

                Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 1, "durationMinutes": 5, "start": "2000-01-01T00:00:00Z" }"""),       "test", out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

                Assert.That(grid.TryAddLocalController(ALocalController("lc-x", "DE*GEF*P9", "nowhere"),   "test", out _, out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

                Assert.That(grid.TryAddLocalController(ALocalController("lc-x", "no pool", "GCP-3"),       "test", out _, out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Invalid));

                Assert.That(grid.TryAddLocalController(ALocalController("LC-NORD", "DE*GEF*P3", "GCP-3"),  "test", out _, out _, out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Conflict));

                Assert.That(grid.TryRemoveGridConnectionPoint("GCP-1", "test", out _, out refusal), Is.False);
                Assert.That(refusal, Is.EqualTo(ChangeRefusal.Conflict));

                Assert.That(grid.TryRemoveGridConnectionPoint("GCP-3", "test", out error, out _), Is.True, error);

            });

        }

        #endregion

        #region LocalControllersAndLimitsAreKept()

        /// <summary>
        /// The local controllers and the limits outlive a restart - and the
        /// secret of a local controller is kept only as its hash.
        /// </summary>
        [Test]
        public async Task LocalControllersAndLimitsAreKept()
        {

            var grid = GridOperatorFrom();

            var (secret, _) = AGridWithTwoPoints(grid);

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 22, "durationMinutes": 30, "gridConnectionPointId": "GCP-2" }"""),
                                          "test", out var limit, out var error, out _), Is.True, error);

            Assert.That(File.ReadAllText(grid.OpenADR.FilePath), Does.Not.Contain(secret));

            var again = await Restarted();

            Assert.Multiple(() => {
                Assert.That(again.OpenADR.Logins().Select(login => login.VENName),  Is.EqualTo(new[] { "lc-nord", "lc-paradies" }), String.Join(" | ", again.OpenADR.ReadProblems));
                Assert.That(again.OpenADR.Event(limit!.Id.ToString())?.VENNames,     Is.EqualTo(new[] { "lc-nord" }));
                Assert.That(again.OpenADR.Event(limit.Id.ToString())?.LimitKW,       Is.EqualTo(22));
                Assert.That(again.OpenADR.TryIssueToken("lc-paradies", secret),      Is.Not.Null, "the secret does not sign in after a restart");
                Assert.That(again.OpenADR.TryIssueToken("lc-paradies", "wrong"),     Is.Null);
            });

        }

        #endregion

        #region ALocalControllerReadsOnlyWhatIsMeantForIt()

        /// <summary>
        /// Over HTTP, as OpenADR 3 has it: a token for the client credentials -
        /// as a form or as HTTP Basic - and with it the program, the limits
        /// meant for this local controller and no other, itself as its VEN, and
        /// a report on one of its limits. Nothing without the token, nothing
        /// with a secret that was replaced, and no program made by a VEN.
        /// </summary>
        [Test]
        public async Task ALocalControllerReadsOnlyWhatIsMeantForIt()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            var (secret, nordSecret) = AGridWithTwoPoints(grid);

            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 44, "durationMinutes": 60, "gridConnectionPointId": "GCP-1" }"""), "test", out var mine,  out var error, out _), Is.True, error);
            Assert.That(grid.TrySendLimit(JObject.Parse("""{ "limitKW": 11, "durationMinutes": 60, "gridConnectionPointId": "GCP-2" }"""), "test", out var other, out error,     out _), Is.True, error);

            using var vtn = Anonymous();

            #region No token, no answer

            var (status, _) = await Send(vtn, HttpMethod.Get, "openadr3/events");
            Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));

            var wrong = await vtn.PostAsync("openadr3/auth/token", new FormUrlEncodedContent(new Dictionary<String, String> {
                                                                       { "grant_type",     "client_credentials" },
                                                                       { "client_id",      "lc-paradies" },
                                                                       { "client_secret",  nordSecret }
                                                                   }));
            Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            #endregion

            #region The token

            var answer  = await vtn.PostAsync("openadr3/auth/token", new FormUrlEncodedContent(new Dictionary<String, String> {
                                                                         { "grant_type",     "client_credentials" },
                                                                         { "client_id",      "lc-paradies" },
                                                                         { "client_secret",  secret }
                                                                     }));
            var token   = JObject.Parse(await answer.Content.ReadAsStringAsync());

            Assert.That(answer.StatusCode,                         Is.EqualTo(HttpStatusCode.OK), token.ToString());
            Assert.That(token["token_type"]?.Value<String>(),      Is.EqualTo("Bearer"));

            using var basic = Anonymous();
            basic.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"lc-paradies:{secret}")));
            Assert.That((await basic.PostAsync("openadr3/auth/token", new StringContent("grant_type=client_credentials", Encoding.UTF8, "application/x-www-form-urlencoded"))).StatusCode,
                        Is.EqualTo(HttpStatusCode.OK), "HTTP Basic is not taken");

            vtn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token["access_token"]!.Value<String>());

            #endregion

            #region What it reads

            var (programsStatus, programs) = await Send(vtn, HttpMethod.Get, "openadr3/programs");
            var (eventsStatus,   events)   = await Send(vtn, HttpMethod.Get, "openadr3/events");
            var (vensStatus,     vens)     = await Send(vtn, HttpMethod.Get, "openadr3/vens");
            var (otherStatus,    _)        = await Send(vtn, HttpMethod.Get, $"openadr3/events/{other!.Id}");

            Assert.Multiple(() => {
                Assert.That(programsStatus,                                    Is.EqualTo(HttpStatusCode.OK));
                Assert.That(programs[0]?["programName"]?.Value<String>(),      Is.EqualTo(GridOperator.ProgramName));
                Assert.That(eventsStatus,                                      Is.EqualTo(HttpStatusCode.OK));
                Assert.That(events.Select(one => one["id"]!.Value<String>()),  Is.EqualTo(new[] { mine!.Id.ToString() }), "it reads another one's limit");
                Assert.That(vensStatus,                                        Is.EqualTo(HttpStatusCode.OK));
                Assert.That(vens.Select(one => one["venName"]!.Value<String>()), Is.EqualTo(new[] { "lc-paradies" }));
                Assert.That(otherStatus,                                       Is.EqualTo(HttpStatusCode.NotFound));
            });

            #endregion

            #region What it writes

            var report = JObject.Parse($$"""
                             { "programID": "{{GridOperator.ProgramName}}", "eventID": "{{mine.Id}}", "clientName": "somebody else",
                               "resources": [ { "resourceName": "pool", "intervals": [ { "id": 0, "payloads": [ { "type": "USAGE", "values": [ 41.5 ] } ] } ] } ] }
                         """);

            var (reportStatus,  reported)  = await Send(vtn, HttpMethod.Post, "openadr3/reports",  report);
            var (programStatus, _)         = await Send(vtn, HttpMethod.Post, "openadr3/programs", new JObject());

            Assert.Multiple(() => {
                Assert.That(reportStatus,                                Is.EqualTo(HttpStatusCode.Created), reported.ToString());
                Assert.That(reported["clientName"]?.Value<String>(),     Is.EqualTo("lc-paradies"), "a report is kept under another name than its sender's");
                Assert.That(grid.OpenADR.Reports(),                      Has.Count.EqualTo(1));
                Assert.That(programStatus,                               Is.EqualTo(HttpStatusCode.Forbidden));
            });

            #endregion

            #region A new secret: the token signs in no more

            Assert.That(grid.TryNewSecret("lc-paradies", "test", out _, out _, out error, out _), Is.True, error);

            (status, _) = await Send(vtn, HttpMethod.Get, "openadr3/events");
            Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));

            #endregion

        }

        #endregion

    }

}
