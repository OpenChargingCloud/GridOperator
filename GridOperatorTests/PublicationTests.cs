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

using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json.Linq;

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

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

        #region (helper) Key(Seed, Curve)

        /// <summary>
        /// A key of its own for every seed, the same every time.
        /// </summary>
        private static (ECPrivateKeyParameters Private, POIPublicKey Public) Key(String     Seed,
                                                                                 COSECurve  Curve)
        {

            var parameters  = Curve.DomainParameters!;
            var d           = new BigInteger(1, SHA512.HashData(Encoding.UTF8.GetBytes(Seed))).Mod(parameters.N.Subtract(BigInteger.One)).Add(BigInteger.One);
            var privateKey  = new ECPrivateKeyParameters(d, parameters);

            return (privateKey, new POIPublicKey(COSEKey.From(privateKey)));

        }

        #endregion

        #region (helper) OperatedNetwork()

        private static readonly (ECPrivateKeyParameters Private, POIPublicKey Public)  OperatorSigningKey   = Key("QA-RNO:signing",  COSECurve.P256);
        private static readonly (ECPrivateKeyParameters Private, POIPublicKey Public)  OperatorIdentityKey  = Key("QA-RNO:identity", COSECurve.P521);

        /// <summary>
        /// The roaming network QA as a hub operates it: an operator with an
        /// identity key, a signing key certified by it, and a key statement -
        /// whose "signedAt" and "validUntil" are texts that look like dates.
        /// </summary>
        private static RoamingNetwork OperatedNetwork()
        {

            var operatorId  = RoamingNetworkOperator_Id.Parse("QA-RNO");
            var created     = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
            var json        = new RoamingNetwork(RoamingNetwork_Id.Parse("QA"),
                                                 I18NString.Create(Languages.en, "Quality assurance"),
                                                 Created: created).ToJSON();

            json[RoamingNetwork.PropertyKeys.Operator] = new JObject(
                new JProperty("@id",                                          operatorId.ToString()),
                new JProperty("created",                                      json["created"]),
                new JProperty("lastChange",                                   json["created"]),
                new JProperty(POIPartyKeys.IdentityKeysProperty,              new JArray(OperatorIdentityKey.Public.ToJSON(Embedded: true))),
                new JProperty(RoamingNetworkSigningKeyCertificate.JSONProperty, new JArray(RoamingNetworkSigningKeyCertificate.Create(OperatorIdentityKey.Private,
                                                                                                                                     COSEAlgorithm.ES512,
                                                                                                                                     operatorId,
                                                                                                                                     OperatorSigningKey.Public,
                                                                                                                                     created).ToJSON(Embedded: true))),
                new JProperty(RoamingNetworkKeyStatement.JSONProperty,        RoamingNetworkKeyStatement.Create(OperatorIdentityKey.Private,
                                                                                                                COSEAlgorithm.ES512,
                                                                                                                1,
                                                                                                                RoamingNetwork_Id.Parse("QA"),
                                                                                                                operatorId,
                                                                                                                [ OperatorIdentityKey.Public ],
                                                                                                                created,
                                                                                                                DateTimeOffset.Parse("2036-01-01T00:00:00Z")).ToJSON(Embedded: true))
            );

            Assert.That(RoamingNetwork.TryParse(json, out var network, out var error), Is.True, error);

            return network!;

        }

        #endregion

        #region (helper) Addition(Network, GridOperator)

        /// <summary>
        /// The change set the operator of the roaming network adds the given
        /// grid operator with, signed by it - as a hub makes it.
        /// </summary>
        private static RoamingNetworkChangeSet Addition(RoamingNetwork            Network,
                                                        params POIGridOperator[]  GridOperators)

            => Network.CreateChangeSet(GridOperators.Select(RoamingNetworkChange.Add).ToArray()).
                       Sign(OperatorSigningKey.Private, OperatorSigningKey.Public.Id, COSEAlgorithm.ES256);

        #endregion

        #region (helper) AsAdded(GridOperator, Change = null)

        /// <summary>
        /// The grid operator as its party is added: without its points.
        /// </summary>
        private static POIGridOperator AsAdded(GridOperator           Grid,
                                               Action<JObject>?       Change = null)
        {

            var json = Grid.Own.Entity.ToJSON();
            json.Remove("gridConnectionPoints");

            Change?.Invoke(json);

            Assert.That(POIGridOperator.TryParse(json, out var party, out var error), Is.True, error);

            return party!;

        }

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

        #region AddedByTheHubAcceptedAndPublished()

        /// <summary>
        /// The roaming network QA as a hub operates it - with the key statement
        /// of its operator - on the POI API beside the grid operator: its head is
        /// read, as is the revision of its head commit; nothing is published nor
        /// accepted while QA does not know it. Its operator adds it, with a change
        /// set that waits for the identity key of the grid operator; the preview
        /// says so, accepting signs it, and it is committed - and then its point
        /// is published.
        /// </summary>
        [Test]
        public async Task AddedByTheHubAcceptedAndPublished()
        {

            var grid = await TestPorts.StartedOnFreshPorts(() => GridOperatorFrom());

            Assert.That(grid.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);

            var history  = RoamingNetworkHistory.Start(OperatedNetwork());
            var poi      = new POI_API(grid.HTTPServer, RootPath: HTTPPath.Parse("/poi")) {
                               AuthorizeChangeSetSubmission = (request, changeSet) => true
                           };

            Assert.That(poi.AddRoamingNetworkHistory(history), Is.True);
            Assert.That(grid.TryUpdateRoamingNetworkSettings(new JObject(new JProperty("url", $"{address}poi/RNs/QA")), "test", out error, out _), Is.True, error);

            #region Not a party yet: the head is read, nothing is published, nothing accepted

            var before = await grid.PreviewPublication();

            Assert.Multiple(() => {
                Assert.That(before.Success,          Is.True, before.Message);
                Assert.That(before.Plan!.IsParty,    Is.False);
                Assert.That(before.Awaiting,         Is.Empty);
                Assert.That(before.Revision,         Is.EqualTo(history.Head.Commit.Revision));
            });

            var refused = await grid.Publish("test");

            Assert.That(refused.Refusal, Is.EqualTo(ChangeRefusal.Conflict), refused.Message);

            var nothingToAccept = await grid.AcceptAddition("test");

            Assert.Multiple(() => {
                Assert.That(nothingToAccept.Success,  Is.False);
                Assert.That(nothingToAccept.Refusal,  Is.EqualTo(ChangeRefusal.Conflict));
                Assert.That(nothingToAccept.Message,  Does.Contain("No change set adding it waits there"));
            });

            #endregion

            #region Its operator adds it: the change set waits for its identity key

            var addition  = Addition(history.Head.RoamingNetwork, AsAdded(grid));
            var waiting   = history.CoSign(addition);

            Assert.That(waiting.Status, Is.EqualTo(RoamingNetworkCoSigningStatus.Pending), waiting.ErrorResponse);

            var offered = await grid.PreviewPublication();

            Assert.Multiple(() => {
                Assert.That(offered.Awaiting,  Is.EqualTo(new[] { addition.Id.ToString() }));
                Assert.That(offered.Message,   Does.Contain("accepting signs it"));
            });

            #endregion

            #region Accepted: committed, and the point published after it

            var accepted = await grid.AcceptAddition("test");

            Assert.That(accepted.Success, Is.True, accepted.Message + " " + accepted.Answer);

            Assert.Multiple(() => {
                Assert.That(history.Head.RoamingNetwork.GridOperators.TryGet(grid.Own.Id, out _),  Is.True);
                Assert.That(history.PendingChangeSets,                                             Is.Empty);
                Assert.That(accepted.Plan!.IsParty,                                                Is.True);
                Assert.That(accepted.Revision,                                                     Is.EqualTo(history.Head.Commit.Revision));
            });

            var again = await grid.AcceptAddition("test");

            Assert.That(again.Message, Does.Contain("there is nothing to accept"));

            var published = await grid.Publish("test");

            Assert.That(published.Success, Is.True, published.Message + " " + published.Answer);

            Assert.Multiple(() => {
                Assert.That(history.Head.RoamingNetwork.GridOperators.TryGet(grid.Own.Id, out var there),  Is.True);
                Assert.That(there!.GridConnectionPoints.Select(point => point.Id.ToString()),              Is.EqualTo(new[] { "GCP-1" }));
                Assert.That(published.Revision,                                                            Is.EqualTo(history.Head.Commit.Revision));
            });

            #endregion

        }

        #endregion

        #region ItsIdentityKeyVouchesForNothingButItself()

        /// <summary>
        /// The identity key of the grid operator signs the change set that adds
        /// it with its own keys - and no other: not one that gives it a key not
        /// its own, adds another party beside it, or that it signed already.
        /// </summary>
        [Test]
        public void ItsIdentityKeyVouchesForNothingButItself()
        {

            var grid     = GridOperatorFrom();
            var network  = OperatedNetwork();
            var foreign  = Key("someone else:signing", COSECurve.P256).Public;
            var addition = Addition(network, AsAdded(grid));

            Assert.Multiple(() => {

                Assert.That(PendingAddition.WhyNotToSign(network, addition, grid.Own),
                            Is.Null);

                Assert.That(PendingAddition.WhyNotToSign(network, grid.Own.SignWithIdentityKey(addition), grid.Own),
                            Does.Contain("signed with the identity key of the grid operator 'DE*SWJ' already"));

                Assert.That(PendingAddition.WhyNotToSign(network,
                                                         Addition(network, AsAdded(grid, json => json["signingKeys"] = new JArray(foreign.ToJSON(Embedded: true)))),
                                                         grid.Own),
                            Does.Contain($"the signing key '{foreign.Id}', which is not its own"));

                Assert.That(PendingAddition.WhyNotToSign(network,
                                                         Addition(network, AsAdded(grid), new POIGridOperator(GridOperator_Id.Parse("DE*XYZ"))),
                                                         grid.Own),
                            Does.Contain("changes the GridOperator 'DE*XYZ' as well"));

                Assert.That(PendingAddition.WhyNotToSign(network,
                                                         Addition(network, AsAdded(grid, json => json.Remove("identityKeys"))),
                                                         grid.Own),
                            Is.Not.Null);

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
