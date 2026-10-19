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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.GridOperator.POI;

using POIGridOperator = cloud.charging.open.protocols.WWCP.POI.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// What publishing the grid operator to a roaming network changes there:
    /// its own properties and the points it maintains - and the change set,
    /// signed with its key, is one the roaming network takes, after which it
    /// says what this node says.
    /// </summary>
    public class PublicationTests : AGridOperatorTests
    {

        #region (helper) NetworkWith(GridOperator)

        private static RoamingNetwork NetworkWith(POIGridOperator GridOperator)

            => new (RoamingNetwork_Id.Parse("QA"),
                    I18NString.Create(Languages.en, "Quality assurance"),
                    Created:        DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    GridOperators:  [ GridOperator ]);

        #endregion


        #region ThePlanBringsTheRoamingNetworkToWhatThisNodeSays()

        /// <summary>
        /// The roaming network has the grid operator as it was added - a
        /// homepage of old, one point of it changed, one it no longer has and
        /// one a charging station operator maintains - and this node has one
        /// more point; the plan updates, adds and removes what is the grid
        /// operator's, leaves the other one's alone, and the network takes it.
        /// </summary>
        [Test]
        public void ThePlanBringsTheRoamingNetworkToWhatThisNodeSays()
        {

            var grid = GridOperatorFrom();

            Assert.That(grid.TryUpdateGridOperator(JObject.Parse("""{ "homepage": "https://netz.example.org" }"""), "test", out var error, out _), Is.True, error);
            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1", 110000), "test", out _, out error, out _), Is.True, error);
            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-2",  50000), "test", out _, out error, out _), Is.True, error);

            #region What the roaming network has

            var theirsJSON = grid.Own.Entity.ToJSON();

            theirsJSON["homepage"]              = "https://old.example.org";
            theirsJSON["gridConnectionPoints"]  = new JArray(
                                                      APoint("GCP-1", 90000),
                                                      APoint("GCP-OLD")
                                                  );

            Assert.That(POIGridOperator.TryParse(theirsJSON, out var theirs, out error), Is.True, error);

            var network = NetworkWith(theirs!);

            #endregion

            var plan = PublicationPlan.Of(network, grid.Own.Entity);

            Assert.Multiple(() => {
                Assert.That(plan.IsParty, Is.True);
                Assert.That(plan.Steps.Select(step => $"{step.Kind} {step.EntityId} {step.Property}".Trim()),
                            Is.EqualTo(new[] {
                                "update DE*SWJ homepage",
                                "update GCP-1 contractedImportApparentPower",
                                "update GCP-1 contractedImportPower",
                                "add GCP-2",
                                "remove GCP-OLD"
                            }));
            });

            var changeSet = grid.Own.Sign(network.CreateChangeSet(plan.Changes));

            Assert.That(network.TryApplyChangeSet(changeSet, out var after, out error), Is.True, error);

            Assert.That(after!.GridOperators.TryGet(grid.Own.Id, out var published), Is.True);

            Assert.Multiple(() => {
                Assert.That(published!.Homepage?.ToString(),                                           Is.EqualTo("https://netz.example.org"));
                Assert.That(published.GridConnectionPoints.Select(point => point.Id.ToString()),     Is.EqualTo(new[] { "GCP-1", "GCP-2" }));
                Assert.That(PublicationPlan.Of(after, grid.Own.Entity).IsEmpty,                      Is.True, "published, and still something to publish");
            });

        }

        #endregion

        #region PublishedOverHTTPToTheAPIAHubServes()

        /// <summary>
        /// The POI API a roaming hub serves, here beside the grid operator on
        /// its own HTTP server, with the roaming network QA that has the grid
        /// operator as a party: what publishing would change is asked of it,
        /// the change set goes to it by COMMIT, signed by the grid operator -
        /// and its head says then what this node says.
        /// </summary>
        [Test]
        public async Task PublishedOverHTTPToTheAPIAHubServes()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);

            #region The hub's side: QA, with the grid operator as it was added - without its points

            var party = grid.Own.Entity.ToJSON();
            party.Remove("gridConnectionPoints");

            Assert.That(POIGridOperator.TryParse(party, out var added, out error), Is.True, error);

            var history  = RoamingNetworkHistory.Start(NetworkWith(added!));
            var poi      = new POI_API(grid.HTTPServer, RootPath: HTTPPath.Parse("/poi")) {
                               AuthorizeChangeSetSubmission = (request, changeSet) => true
                           };

            Assert.That(poi.AddRoamingNetworkHistory(history), Is.True);

            #endregion

            Assert.That(grid.TryUpdateRoamingNetworkSettings(new JObject(new JProperty("url", $"{address}poi/RNs/QA")), "test", out error, out _), Is.True, error);

            var preview = await grid.PreviewPublication();

            Assert.That(preview.Success,                                   Is.True, preview.Message);
            Assert.That(preview.Plan!.Steps.Select(step => step.Kind),     Is.EqualTo(new[] { "add" }));

            var published = await grid.Publish("test");

            Assert.That(published.Success, Is.True, published.Message + " " + published.Answer);

            Assert.Multiple(() => {
                Assert.That(history.Head.Commit.Revision,                                                    Is.EqualTo(1));
                Assert.That(history.Head.RoamingNetwork.GridOperators.TryGet(grid.Own.Id, out var there),    Is.True);
                Assert.That(there!.GridConnectionPoints.Select(point => point.Id.ToString()),                Is.EqualTo(new[] { "GCP-1" }));
            });

            var again = await grid.PreviewPublication();

            Assert.That(again.Plan!.IsEmpty, Is.True, "published, and still something to publish");

        }

        #endregion

        #region NotAPartyNothingToSign()

        /// <summary>
        /// A roaming network that has not added this grid operator yet: nothing
        /// is signed, as nothing of it is there to change.
        /// </summary>
        [Test]
        public void NotAPartyNothingToSign()
        {

            var grid     = GridOperatorFrom();
            var stranger = new POIGridOperator(GridOperator_Id.Parse("DE*XYZ"));

            var plan     = PublicationPlan.Of(NetworkWith(stranger), grid.Own.Entity);

            Assert.Multiple(() => {
                Assert.That(plan.IsParty, Is.False);
                Assert.That(plan.Changes, Is.Empty);
            });

        }

        #endregion

        #region ThePartyHasThePublicKeysOnly()

        /// <summary>
        /// What the operator of a roaming network gets: the identification,
        /// the name and the public halves of both keys - which POI 3 takes as a
        /// grid operator - and nothing else.
        /// </summary>
        [Test]
        public void ThePartyHasThePublicKeysOnly()
        {

            var grid   = GridOperatorFrom();
            var party  = grid.Own.PartyJSON();

            Assert.Multiple(() => {
                Assert.That(party.Properties().Select(property => property.Name), Is.EqualTo(new[] { "@id", "name", "signingKeys", "identityKeys" }));
                Assert.That(party["signingKeys"]![0]!.Value<String>(),            Is.EqualTo(grid.Own.SigningKey. ToJSON()["key"]!.Value<String>()));
                Assert.That(party["identityKeys"]![0]!.Value<String>(),           Is.EqualTo(grid.Own.IdentityKey.ToJSON()["key"]!.Value<String>()));
                Assert.That(Directory.GetFiles(grid.Own.Directory).Select(Path.GetFileName).Order(),
                            Is.EqualTo(new[] { OwnGridOperator.OperatorFileName, OwnGridOperator.IdentityKeyFileName, OwnGridOperator.SigningKeyFileName }.Order()));
                Assert.That(File.ReadAllText(Path.Combine(grid.Own.Directory, OwnGridOperator.OperatorFileName)), Does.Not.Contain("PRIVATE"));
            });

        }

        #endregion

    }

}
