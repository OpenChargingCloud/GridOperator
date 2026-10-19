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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.OpenADRv3;

using OpenADRValueType = cloud.charging.open.protocols.OpenADRv3.ValueType;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// A local controller of a charging pool on this grid, as the virtual end
    /// node (VEN) of OpenADR 3 that may sign in here: its name, which is its
    /// client identification as well, the pool it controls, the grid connection
    /// point that pool is connected to, and the hash of its client secret.
    /// </summary>
    /// <remarks>
    /// The secret itself is shown once, when it is made, and never kept: what
    /// is kept is a salt and the SHA-256 of salt and secret, compared in
    /// constant time. A secret lost is a new one made, which the old one stops
    /// working with.
    /// </remarks>
    public sealed partial record LocalControllerLogin(VirtualEndNode_Id  VENId,
                                                      String             VENName,
                                                      String             ChargingPoolId,
                                                      String             GridConnectionPointId,
                                                      String?            Description,
                                                      String             SecretSalt,
                                                      String             SecretHash,
                                                      DateTimeOffset     Created)
    {

        #region Data

        /// <summary>
        /// The longest name taken.
        /// </summary>
        public const Int32  MaxNameLength  = 64;

        /// <summary>
        /// The attribute of a VEN that names its charging pool.
        /// </summary>
        public const String ChargingPoolAttribute         = "CHARGING_POOL_ID";

        /// <summary>
        /// The attribute of a VEN that names the grid connection point of its charging pool.
        /// </summary>
        public const String GridConnectionPointAttribute  = "GRID_CONNECTION_POINT_ID";

        /// <summary>
        /// The target type of OpenADR that names a VEN.
        /// </summary>
        public const String VENNameTarget                 = "VEN_NAME";

        [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
        private static partial Regex NamePattern();

        #endregion


        #region (static) IsAcceptableName(Name, out Error)

        /// <summary>
        /// Whether a name may be the name of a VEN, and so its client identification:
        /// letters, digits, '.', '_', ':' and '-', at most 64.
        /// </summary>
        public static Boolean IsAcceptableName(String                            Name,
                                               [NotNullWhen(false)] out String?  Error)
        {

            Error = NamePattern().IsMatch(Name)
                        ? null
                        : $"'{Name}' is no name of a local controller: letters, digits, '.', '_', ':' and '-', at most {MaxNameLength} of them.";

            return Error is null;

        }

        #endregion

        #region (static) New(VENName, ChargingPoolId, GridConnectionPointId, Description, Now, out Secret)

        /// <summary>
        /// A new login, and the secret it is signed in with - which is not kept.
        /// </summary>
        public static LocalControllerLogin New(String          VENName,
                                               String          ChargingPoolId,
                                               String          GridConnectionPointId,
                                               String?         Description,
                                               DateTimeOffset  Now,
                                               out String      Secret)
        {

            Secret = NewSecret();

            var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

            return new LocalControllerLogin(VirtualEndNode_Id.NewRandom,
                                            VENName,
                                            ChargingPoolId,
                                            GridConnectionPointId,
                                            Description,
                                            salt,
                                            Hash(salt, Secret),
                                            Now);

        }

        #endregion

        #region WithNewSecret(out Secret)

        /// <summary>
        /// The same login with a new secret: the old one signs in no more.
        /// </summary>
        public LocalControllerLogin WithNewSecret(out String Secret)
        {

            Secret = NewSecret();

            var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

            return this with { SecretSalt = salt, SecretHash = Hash(salt, Secret) };

        }

        #endregion

        #region IsSecret(Secret)

        /// <summary>
        /// Whether the given secret is the secret of this login, compared in constant time.
        /// </summary>
        public Boolean IsSecret(String Secret)

            => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(SecretSalt, Secret)),
                                                       Encoding.ASCII.GetBytes(SecretHash));

        #endregion


        #region ToVirtualEndNode()

        /// <summary>
        /// The login as the VEN object of OpenADR 3: its name, the pool and the
        /// grid connection point as attributes, and its name as its target.
        /// </summary>
        public VirtualEndNode ToVirtualEndNode()

            => new (VENName,
                    Attributes:        [ new ValuesMap(OpenADRValueType.Parse(ChargingPoolAttribute),         [ ChargingPoolId ]),
                                         new ValuesMap(OpenADRValueType.Parse(GridConnectionPointAttribute),  [ GridConnectionPointId ]) ],
                    Targets:           [ new ValuesMap(OpenADRValueType.Parse(VENNameTarget),                 [ VENName ]) ],
                    Id:                VENId,
                    Created:           Created,
                    LastModification:  Created);

        #endregion

        #region ToJSON(IncludeSecret)

        /// <summary>
        /// The login as it is kept, with the hash of its secret - or as the
        /// JSON API shows it, without.
        /// </summary>
        public JObject ToJSON(Boolean IncludeSecret)
        {

            var json = new JObject(
                           new JProperty("venID",                  VENId.ToString()),
                           new JProperty("venName",                VENName),
                           new JProperty("chargingPoolId",         ChargingPoolId),
                           new JProperty("gridConnectionPointId",  GridConnectionPointId),
                           new JProperty("created",                Created.ToISO8601())
                       );

            if (Description is not null)
                json.Add(new JProperty("description", Description));

            if (IncludeSecret)
                json.Add(new JProperty("secret", new JObject(
                                                     new JProperty("salt",    SecretSalt),
                                                     new JProperty("sha256",  SecretHash)
                                                 )));

            return json;

        }

        #endregion

        #region (static) TryParse(JSON, out Login, out Error)

        /// <summary>
        /// A login as it is kept.
        /// </summary>
        public static Boolean TryParse(JObject                                         JSON,
                                       [NotNullWhen(true)]  out LocalControllerLogin?  Login,
                                       [NotNullWhen(false)] out String?                Error)
        {

            Login = null;

            var venId       = JSON.Value<String>("venID");
            var venName     = JSON.Value<String>("venName");
            var poolId      = JSON.Value<String>("chargingPoolId");
            var pointId     = JSON.Value<String>("gridConnectionPointId");
            var salt        = JSON["secret"]?.Value<String>("salt");
            var hash        = JSON["secret"]?.Value<String>("sha256");
            var created     = JSON.Value<String>("created");

            if (String.IsNullOrEmpty(venId) || String.IsNullOrEmpty(venName) || String.IsNullOrEmpty(poolId) ||
                String.IsNullOrEmpty(pointId) || String.IsNullOrEmpty(salt)  || String.IsNullOrEmpty(hash)   ||
                !DateTimeOffset.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
            {
                Error = "A local controller is kept with its 'venID', 'venName', 'chargingPoolId', 'gridConnectionPointId', 'secret' and 'created'.";
                return false;
            }

            if (!IsAcceptableName(venName, out Error))
                return false;

            Login = new LocalControllerLogin(VirtualEndNode_Id.Parse(venId),
                                             venName,
                                             poolId,
                                             pointId,
                                             JSON.Value<String>("description"),
                                             salt,
                                             hash,
                                             createdAt);

            return true;

        }

        #endregion


        #region (private static) NewSecret() / Hash(Salt, Secret)

        private static String NewSecret()

            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).
                       Replace('+', '-').
                       Replace('/', '_').
                       TrimEnd('=');

        private static String Hash(String Salt, String Secret)

            => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + ":" + Secret)));

        #endregion

    }

}
