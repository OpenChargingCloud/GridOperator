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

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.GridOperator.Tests
{

    /// <summary>
    /// What the tests of a grid operator share: a directory of its own for
    /// each test, a grid operator made in it - started or not - and clients
    /// signed in to its JSON API in a role.
    /// </summary>
    public abstract class AGridOperatorTests
    {

        #region Data

        /// <summary>
        /// A configuration without time servers, and with the grid operator it
        /// is to be at its first start.
        /// </summary>
        protected const String  Configuration  = """{ "nts": { "groups": { "legal": { "enabled": false } } }, "gridOperator": { "@id": "DE*SWJ", "name": "Netze Beispielstadt" } }""";

        protected String         directory     = "";
        protected GridOperator?  gridOperator;
        protected Uri?           address;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "gridoperator-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public async Task TearDown()
        {

            if (gridOperator is not null)
                await gridOperator.DisposeAsync();

            gridOperator = null;

            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // A temporary directory that outlives one test run is not worth
                // failing the run over.
            }

        }

        #endregion


        #region (protected) GridOperatorFrom(ConfigurationText = null)

        /// <summary>
        /// A grid operator in the directory of this test, on a free port of the
        /// loopback - made, and not yet started. Made again in the same
        /// directory, it is the one made before: its keys, its grid connection
        /// points and its local controllers are kept there.
        /// </summary>
        protected GridOperator GridOperatorFrom(String? ConfigurationText = null)
        {

            var port  = TestPorts.Free();
            var file  = Path.Combine(directory, WWCPConfigFile.DefaultFileName);

            if (!File.Exists(file) || ConfigurationText is not null)
                File.WriteAllText(file, ConfigurationText ?? Configuration);

            gridOperator  = new GridOperator(
                                HTTPPort:          IPPort.Parse(port),
                                AccountsPath:      Path.Combine(directory, "accounts"),
                                ConfigFile:        new WWCPConfigFile(file),
                                CertificatesPath:  Path.Combine(directory, "certificates"),
                                LogToConsole:      false,
                                BridgeDebugLog:    false
                            );

            address       = new Uri($"http://127.0.0.1:{port}/");

            return gridOperator;

        }

        #endregion

        #region (protected) Restarted()

        /// <summary>
        /// The grid operator stopped, and made anew in the same directory.
        /// </summary>
        protected async Task<GridOperator> Restarted()
        {

            if (gridOperator is not null)
                await gridOperator.DisposeAsync();

            gridOperator = null;

            return GridOperatorFrom();

        }

        #endregion

        #region (protected) SignedInAs(Name, Role)

        /// <summary>
        /// A client signed in with a password as an account made for the
        /// purpose, in the group of the given role.
        /// </summary>
        protected async Task<HttpClient> SignedInAs(String Name, String Role)
        {

            var password = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(gridOperator!.ExtAPI.TryGetOrganization(Organization_Id.Parse("GridOperator"), out var organization) &&
                        organization is Organization, Is.True, "the grid operator's organization is not there");

            var account = await gridOperator.ExtAPI.CreateUser(
                                    User_Id.Parse(Name),
                                    I18NString.Create(Languages.en, Name),
                                    SimpleEMailAddress.Parse($"{Name}@localhost"),
                                    User2OrganizationEdgeLabel.IsMember,
                                    (Organization) organization!,
                                    Password:                  password,
                                    SkipDefaultNotifications:  true,
                                    SkipNewUserEMail:          true,
                                    SkipNewUserNotifications:  true,
                                    AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                    IsAuthenticated:           true
                                );

            Assert.That(account, Is.Not.Null);
            Assert.That(gridOperator.ExtAPI.TryGetUser(User_Id.Parse(Name), out var stored), Is.True);
            Assert.That(gridOperator.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group), Is.True, $"the grid operator has no group '{Role}'");
            Assert.That((await gridOperator.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!)).IsSuccess, Is.True);

            var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                             "Basic",
                                                             Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Name}:{password}"))
                                                         );

            return client;

        }

        #endregion

        #region (protected) Anonymous()

        /// <summary>
        /// A client signed in as nobody - a local controller, before it has its token.
        /// </summary>
        protected HttpClient Anonymous()

            => new () { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };

        #endregion

        #region (protected static) Send(Client, Method, Path, JSON = null)

        /// <summary>
        /// A request with a JSON body, and its answer as JSON.
        /// </summary>
        protected static async Task<(HttpStatusCode Status, JToken JSON)> Send(HttpClient  Client,
                                                                               HttpMethod  Method,
                                                                               String      Path,
                                                                               JToken?     JSON = null)
        {

            using var request = new HttpRequestMessage(Method, Path);

            if (JSON is not null)
                request.Content = new StringContent(JSON.ToString(), Encoding.UTF8, "application/json");

            var response = await Client.SendAsync(request);
            var text     = await response.Content.ReadAsStringAsync();

            return (response.StatusCode, text.Length > 0 ? JToken.Parse(text) : new JObject());

        }

        #endregion

        #region (protected static) APoint(Id, ImportPowerW = 110000)

        /// <summary>
        /// A grid connection point, as POI 3 writes one.
        /// </summary>
        protected static JObject APoint(String  Id,
                                        Int32   ImportPowerW = 110000)

            => new (new JProperty("@id",                            Id),
                    new JProperty("name",                           new JObject(new JProperty("de", $"Netzanschluss {Id}"))),
                    new JProperty("voltageLevel",                   "LowVoltage"),
                    new JProperty("connectionType",                 "AC_ThreePhases"),
                    new JProperty("nominalVoltage",                 400),
                    new JProperty("nominalFrequency",               50),
                    new JProperty("contractedImportPower",          ImportPowerW),
                    new JProperty("contractedImportApparentPower",  ImportPowerW + 10000),
                    new JProperty("marketLocationIds",              new JArray("50123456789")));

        #endregion

        #region (protected static) ALocalController(VENName, PoolId, PointId)

        /// <summary>
        /// A local controller to be added, as the JSON API takes one.
        /// </summary>
        protected static JObject ALocalController(String VENName, String PoolId, String PointId)

            => new (new JProperty("venName",                VENName),
                    new JProperty("chargingPoolId",         PoolId),
                    new JProperty("gridConnectionPointId",  PointId));

        #endregion

    }

}
