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
using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Certificates;

using cloud.charging.open.GridOperator.POI;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// What kind of node a grid operator is - its names, its port, the
    /// certificates it keeps - and the grid operator it is in POI 3: made at
    /// the first start as the configuration file says, and the same one at
    /// every start after it.
    /// </summary>
    public class GridOperatorTests : AGridOperatorTests
    {

        #region TheKind()

        /// <summary>
        /// The names a grid operator goes by - and the organization of its
        /// accounts above all, which a first start writes down for good.
        /// </summary>
        [Test]
        public void TheKind()
        {

            var kind = GridOperator.GridOperatorKind;

            Assert.Multiple(() => {
                Assert.That(kind.Name,             Is.EqualTo("grid operator"));
                Assert.That(kind.CapitalisedName,  Is.EqualTo("Grid operator"));
                Assert.That(kind.Tag,              Is.EqualTo("gridOperator"));
                Assert.That(kind.Product,          Is.EqualTo("GridOperator"));
                Assert.That(kind.Organization,     Is.EqualTo("GridOperator"));
                Assert.That(kind.LogFilePrefix,    Is.EqualTo("gridoperator"));
                Assert.That(GridOperator.DefaultHTTPPort, Is.EqualTo(IPPort.Parse(2358)));
            });

        }

        #endregion

        #region WhatItKeeps()

        /// <summary>
        /// The roots it believes, the server certificates it holds a server to,
        /// who it is as a client and the identity its web interface shows over
        /// HTTPS - and neither a root of clients nor anything of ISO 15118.
        /// </summary>
        [Test]
        public void WhatItKeeps()
        {

            var store = GridOperatorFrom().Certificates;

            Assert.Multiple(() => {
                Assert.That(store.Kinds, Is.EqualTo(new[] { CertificateKind.TLSRoot, CertificateKind.TLSServer, CertificateKind.TLSIdentity, CertificateKind.TLSServerIdentity }));
                Assert.That(store.Kinds.Intersect(CertificateKindExtensions.ISO15118), Is.Empty);
            });

        }

        #endregion

        #region TheFirstStartMakesItAndEveryOtherReadsItBack()

        /// <summary>
        /// The first start makes the grid operator the configuration file
        /// names, with new keys; the next start reads the same one back - its
        /// keys, its name and its grid connection points - and does not read
        /// the section again.
        /// </summary>
        [Test]
        public async Task TheFirstStartMakesItAndEveryOtherReadsItBack()
        {

            var first = GridOperatorFrom();

            Assert.Multiple(() => {
                Assert.That(first.Own.IsNew,                        Is.True);
                Assert.That(first.Own.Id.ToString(),                Is.EqualTo("DE*SWJ"));
                Assert.That(first.Own.Entity.Name?.FirstText(),     Is.EqualTo("Netze Beispielstadt"));
            });

            Assert.That(first.TryAddGridConnectionPoint(APoint("GCP-1"), "test", out _, out var error, out _), Is.True, error);

            var signingKey   = first.Own.SigningKey.Id;
            var identityKey  = first.Own.IdentityKey.Id;

            File.WriteAllText(Path.Combine(directory, "configuration.json"), """{ "nts": { "groups": { "legal": { "enabled": false } } }, "gridOperator": { "@id": "DE*ABC" } }""");

            var again = await Restarted();

            Assert.Multiple(() => {
                Assert.That(again.Own.IsNew,                                    Is.False);
                Assert.That(again.Own.Id.ToString(),                            Is.EqualTo("DE*SWJ"), "the section was read again");
                Assert.That(again.Own.SigningKey. Id,                           Is.EqualTo(signingKey));
                Assert.That(again.Own.IdentityKey.Id,                           Is.EqualTo(identityKey));
                Assert.That(again.Own.Entity.GridConnectionPoints.Count,        Is.EqualTo(1));
            });

        }

        #endregion

        #region WithoutTheSectionItNamesItselfByItsKey()

        /// <summary>
        /// No "gridOperator" section: "DE*" and the first three hex digits of
        /// its identity key.
        /// </summary>
        [Test]
        public void WithoutTheSectionItNamesItselfByItsKey()
        {

            var grid = GridOperatorFrom("""{ "nts": { "groups": { "legal": { "enabled": false } } } }""");

            Assert.That(grid.Own.Id.ToString(), Is.EqualTo("DE*" + grid.Own.IdentityKey.Id[..3].ToUpperInvariant()));

        }

        #endregion

        #region AMissingKeyIsNotMadeAgain()

        /// <summary>
        /// A grid operator whose private key is gone does not start: a new key
        /// would be none its roaming network knows.
        /// </summary>
        [Test]
        public async Task AMissingKeyIsNotMadeAgain()
        {

            var grid = GridOperatorFrom();
            var keys = grid.Own.Directory;

            await grid.DisposeAsync();
            gridOperator = null;

            File.Delete(Path.Combine(keys, OwnGridOperator.SigningKeyFileName));

            Assert.That(() => GridOperatorFrom(), Throws.InvalidOperationException.With.Message.Contains("missing"));

        }

        #endregion

        #region ItsOwnPropertiesChangeAndNothingElse()

        /// <summary>
        /// Its name, homepage and hotline change, and an empty one is removed;
        /// its identification and its keys never change here.
        /// </summary>
        [Test]
        public void ItsOwnPropertiesChangeAndNothingElse()
        {

            var grid = GridOperatorFrom();

            Assert.That(grid.TryUpdateGridOperator(JObject.Parse("""{ "homepage": "https://netz.example.org", "hotline": "+49 3641 7654321" }"""), "test", out var error, out _), Is.True, error);
            Assert.That(grid.TryUpdateGridOperator(JObject.Parse("""{ "hotline": "" }"""), "test", out error, out _), Is.True, error);

            Assert.Multiple(() => {
                Assert.That(grid.Own.Entity.Homepage?.ToString(),  Is.EqualTo("https://netz.example.org"));
                Assert.That(grid.Own.Entity.HotlinePhoneNumber,    Is.Null);
                Assert.That(grid.TryUpdateGridOperator(JObject.Parse("""{ "@id": "DE*ABC" }"""),        "test", out _, out _), Is.False);
                Assert.That(grid.TryUpdateGridOperator(JObject.Parse("""{ "signingKeys": [] }"""),      "test", out _, out _), Is.False);
                Assert.That(grid.TryUpdateGridOperator(JObject.Parse("""{ "homepage": "not a url" }"""), "test", out _, out _), Is.False);
            });

        }

        #endregion

        #region TheConfigurationLeadsWithTheGridOperator()

        [Test]
        public void TheConfigurationLeadsWithTheGridOperator()
        {

            var json   = GridOperatorFrom().ConfigurationJSON();
            var first  = json.Properties().First();

            Assert.Multiple(() => {
                Assert.That(first.Name,                                      Is.EqualTo("gridOperator"));
                Assert.That(first.Value["gridOperatorId"]?.Value<String>(),  Is.EqualTo("DE*SWJ"));
                Assert.That(first.Value["VTN"]?.Value<String>(),             Does.EndWith("/openadr3/"));
            });

        }

        #endregion

    }

}
