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
using System.Security.Cryptography;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;

using POIGridOperator = cloud.charging.open.protocols.WWCP.POI.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.POI
{

    /// <summary>
    /// The grid operator this node is, as POI 3 knows it: made at the first
    /// start, with a signing key and an identity key of its own, and read back
    /// at every start after it - with the grid connection points it vouches
    /// for embedded, as every roaming network embeds a grid operator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Below the directory of the configuration file, in "gridOperator":
    /// "gridOperator.json", the grid operator as a roaming network embeds it,
    /// with the public halves of its keys and its grid connection points;
    /// "signing-key.pem", the P-256 key signing its change sets; and
    /// "identity-key.pem", the P-521 key that changes its keys. The private keys
    /// never leave this directory, and only the account the node runs as may
    /// read them.
    /// </para>
    /// <para>
    /// Who it is is said once, at the first start, by the section
    /// "gridOperator" of the configuration file, {"@id", "name"}; without it,
    /// it is "DE*" and the first three hex digits of its identity key. What the
    /// section says later is not read: from then on the file says who it is,
    /// and a roaming network it was added to knows it by that identification.
    /// </para>
    /// <para>
    /// Every change is written whole, to a file beside it first and then in
    /// its place, and only after POI 3 took it as a grid operator: what is kept
    /// is always one POI 3 reads back.
    /// </para>
    /// </remarks>
    public sealed class OwnGridOperator
    {

        #region Data

        /// <summary>
        /// The directory of the grid operator, below the directory of the configuration file.
        /// </summary>
        public const String  DirectoryName          = "gridOperator";

        /// <summary>
        /// The grid operator, as a roaming network embeds it.
        /// </summary>
        public const String  OperatorFileName       = "gridOperator.json";

        /// <summary>
        /// The private P-256 key signing its change sets.
        /// </summary>
        public const String  SigningKeyFileName     = "signing-key.pem";

        /// <summary>
        /// The private P-521 key changing its keys.
        /// </summary>
        public const String  IdentityKeyFileName    = "identity-key.pem";

        /// <summary>
        /// The section of the configuration file that says who it is, at the first start.
        /// </summary>
        public const String  ConfigurationSection   = "gridOperator";

        /// <summary>
        /// The properties of the grid operator itself that may be changed here,
        /// by their JSON keys - what a change set of POI 3 may update or remove
        /// of a grid operator, but its keys and its children.
        /// </summary>
        public static readonly IReadOnlyList<String>  ChangeableProperties  = [ "name", "description", "address", "homepage", "hotline", "dataSource", "customData" ];

        private readonly Lock                    gridOperatorLock = new ();

        private readonly ECPrivateKeyParameters  signingKey;
        private readonly ECPrivateKeyParameters  identityKey;

        private POIGridOperator                  gridOperator;

        #endregion

        #region Properties

        /// <summary>
        /// The grid operator as it is now: with the public halves of its keys,
        /// and its grid connection points.
        /// </summary>
        public POIGridOperator  Entity
        {
            get
            {
                lock (gridOperatorLock)
                    return gridOperator;
            }
        }

        /// <summary>
        /// Its identification, which never changes.
        /// </summary>
        public GridOperator_Id  Id
            => Entity.Id;

        /// <summary>
        /// Where it is kept.
        /// </summary>
        public String           Directory      { get; }

        /// <summary>
        /// Whether it was made now, at this start, rather than read.
        /// </summary>
        public Boolean          IsNew          { get; }

        /// <summary>
        /// The public half of its signing key.
        /// </summary>
        public POIPublicKey     SigningKey     { get; }

        /// <summary>
        /// The public half of its identity key.
        /// </summary>
        public POIPublicKey     IdentityKey    { get; }

        #endregion

        #region Constructor(s)

        private OwnGridOperator(POIGridOperator         GridOperator,
                                ECPrivateKeyParameters  SigningKey,
                                ECPrivateKeyParameters  IdentityKey,
                                String                  Directory,
                                Boolean                 IsNew)
        {

            this.gridOperator  = GridOperator;
            this.signingKey    = SigningKey;
            this.identityKey   = IdentityKey;
            this.SigningKey    = POIPublicKey.From(SigningKey);
            this.IdentityKey   = POIPublicKey.From(IdentityKey);
            this.Directory     = Directory;
            this.IsNew         = IsNew;

        }

        #endregion


        #region (static) OpenOrCreate(Directory, Configuration = null, Now = null)

        /// <summary>
        /// The grid operator kept in the given directory, or, at the first
        /// start, a new one with new keys, kept there from now on.
        /// </summary>
        /// <param name="Directory">Where it is kept.</param>
        /// <param name="Configuration">What the configuration file says of it, read only when it is made.</param>
        /// <param name="Now">When it is made.</param>
        /// <exception cref="InvalidOperationException">What is kept is incomplete or does not fit together.</exception>
        public static OwnGridOperator OpenOrCreate(String           Directory,
                                                   JObject?         Configuration   = null,
                                                   DateTimeOffset?  Now             = null)
        {

            var operatorPath     = Path.Combine(Directory, OperatorFileName);
            var signingKeyPath   = Path.Combine(Directory, SigningKeyFileName);
            var identityKeyPath  = Path.Combine(Directory, IdentityKeyFileName);

            #region Read back

            if (File.Exists(operatorPath))
            {

                if (!POIGridOperator.TryParse(ReadJSON(operatorPath), out var stored, out var error))
                    throw new InvalidOperationException($"'{operatorPath}' is no grid operator: {error}");

                // Without its private keys it can sign nothing any more; made
                // again, it would be somebody else than the one a roaming
                // network knows.
                if (!File.Exists(signingKeyPath) || !File.Exists(identityKeyPath))
                    throw new InvalidOperationException($"A private key of the grid operator '{stored.Id}' is missing from '{Directory}': this node can not sign as it without " +
                                                         "both. Restore them from a copy; a new key would be none its roaming network knows.");

                var storedSigningKey   = ReadKey(signingKeyPath);
                var storedIdentityKey  = ReadKey(identityKeyPath);

                if (!stored.SigningKeys. Any(key => key.Id == POIPublicKey.From(storedSigningKey). Id) ||
                    !stored.IdentityKeys.Any(key => key.Id == POIPublicKey.From(storedIdentityKey).Id))
                    throw new InvalidOperationException($"The private keys in '{Directory}' are not those '{OperatorFileName}' names for the grid operator '{stored.Id}'.");

                return new OwnGridOperator(stored, storedSigningKey, storedIdentityKey, Directory, false);

            }

            #endregion

            #region The first start: made now

            System.IO.Directory.CreateDirectory(Directory);

            // Keys left by a first start that ended before it wrote the grid
            // operator are taken; none are replaced.
            var newSigningKey   = File.Exists(signingKeyPath)  ? ReadKey(signingKeyPath)  : WriteKey(signingKeyPath,  NewKey(COSECurve.P256));
            var newIdentityKey  = File.Exists(identityKeyPath) ? ReadKey(identityKeyPath) : WriteKey(identityKeyPath, NewKey(COSECurve.P521));
            var identityKey     = POIPublicKey.From(newIdentityKey);

            var id              = Configuration?["@id"] is JValue { Type: JTokenType.String } configuredId &&
                                  GridOperator_Id.TryParse(configuredId.Value<String>()!, out var parsedId)
                                      ? parsedId
                                      : GridOperator_Id.Parse("DE*" + identityKey.Id[..3].ToUpperInvariant());

            var name            = Configuration?["name"] switch {
                                      JValue  { Type: JTokenType.String } text  => I18NString.Create(Languages.en, text.Value<String>()!),
                                      JObject                             texts => I18NString.Parse(texts),
                                      _                                         => null
                                  };

            var made            = new POIGridOperator(id,
                                                      name,
                                                      SigningKeys:   [ POIPublicKey.From(newSigningKey) ],
                                                      IdentityKeys:  [ identityKey ],
                                                      Created:       Now ?? Timestamp.Now,
                                                      LastChange:    Now ?? Timestamp.Now);

            // Written last: once it is there, the grid operator is complete.
            WriteOperator(operatorPath, made);

            return new OwnGridOperator(made, newSigningKey, newIdentityKey, Directory, true);

            #endregion

        }

        #endregion


        #region TryChange(Change, Now, out Changed, out Error)

        /// <summary>
        /// Change the grid operator - the JSON a roaming network embeds it as,
        /// handed to the given change - and keep it, if POI 3 still takes it as
        /// a grid operator then.
        /// </summary>
        /// <param name="Change">What to change in the JSON; returns why it can not be changed so, or null.</param>
        /// <param name="Now">When it is changed: its new "lastChange".</param>
        /// <param name="Changed">The grid operator as it is now.</param>
        /// <param name="Error">Why it was not changed.</param>
        /// <param name="NotSaved">True where the change was fine, and the file could not be written.</param>
        public Boolean TryChange(Func<JObject, String?>                     Change,
                                 DateTimeOffset                             Now,
                                 [NotNullWhen(true)]  out POIGridOperator?  Changed,
                                 [NotNullWhen(false)] out String?           Error,
                                 out Boolean                                NotSaved)
        {

            NotSaved = false;

            lock (gridOperatorLock)
            {

                var json = gridOperator.ToJSON();

                Error = Change(json);

                if (Error is not null)
                {
                    Changed = null;
                    return false;
                }

                json["lastChange"] = Now.ToISO8601();

                if (!POIGridOperator.TryParse(json, out Changed, out Error))
                    return false;

                // The keys are this node's to say, and never a page's.
                if (!Changed.SigningKeys. SequenceEqual(gridOperator.SigningKeys) ||
                    !Changed.IdentityKeys.SequenceEqual(gridOperator.IdentityKeys) ||
                     Changed.Id != gridOperator.Id)
                {
                    Changed  = null;
                    Error    = "Neither the identification nor the keys of the grid operator are changed here.";
                    return false;
                }

                try
                {
                    WriteOperator(Path.Combine(Directory, OperatorFileName), Changed);
                }
                catch (Exception e)
                {
                    Changed   = null;
                    Error     = $"'{Path.Combine(Directory, OperatorFileName)}' could not be written, and nothing was changed: {e.Message}";
                    NotSaved  = true;
                    return false;
                }

                gridOperator = Changed;
                return true;

            }

        }

        #endregion

        #region Sign(ChangeSet)

        /// <summary>
        /// The given change set, signed by this grid operator with its signing key.
        /// </summary>
        public RoamingNetworkChangeSet Sign(RoamingNetworkChangeSet ChangeSet)

            => ChangeSet.Sign(signingKey, SigningKey.Id, COSEAlgorithm.ES256);

        #endregion

        #region PartyJSON()

        /// <summary>
        /// What the operator of a roaming network gets to add this grid
        /// operator as a party: its identification, its name and the public
        /// halves of its keys - nothing else, as its grid connection points are
        /// its own to add, with change sets it signs.
        /// </summary>
        public JObject PartyJSON()
        {

            var entity = Entity;
            var json   = new JObject(new JProperty("@id", entity.Id.ToString()));

            if (entity.Name is not null && entity.Name.IsNotNullOrEmpty())
                json.Add(new JProperty("name", entity.Name.ToJSON()));

            json.Add(new JProperty(POIPartyKeys.SigningKeysProperty,   new JArray(entity.SigningKeys. Select(key => key.ToJSON()["key"]))));
            json.Add(new JProperty(POIPartyKeys.IdentityKeysProperty,  new JArray(entity.IdentityKeys.Select(key => key.ToJSON()["key"]))));

            return json;

        }

        #endregion


        #region (private static) NewKey(Curve)

        private static ECPrivateKeyParameters NewKey(COSECurve Curve)
        {
            var generator = new ECKeyPairGenerator();
            generator.Init(new ECKeyGenerationParameters(Curve.DomainParameters!, new SecureRandom()));
            return (ECPrivateKeyParameters) generator.GenerateKeyPair().Private;
        }

        #endregion

        #region (private static) WriteOperator(Path, GridOperator)

        private static void WriteOperator(String           Path,
                                          POIGridOperator  GridOperator)
        {
            var written = Path + ".tmp";
            File.WriteAllText(written, GridOperator.ToJSON().ToString());
            File.Move(written, Path, overwrite: true);
        }

        #endregion

        #region (private static) ReadJSON(Path)

        /// <summary>
        /// The grid operator's file, its timestamps the text they were written
        /// as: read as dates, they came back in the words of this machine.
        /// </summary>
        private static JObject ReadJSON(String Path)
        {
            using var reader = new JsonTextReader(new StringReader(File.ReadAllText(Path))) { DateParseHandling = DateParseHandling.None };
            return JObject.Load(reader);
        }

        #endregion

        #region (private static) WriteKey(Path, Key) / ReadKey(Path)

        private static ECPrivateKeyParameters WriteKey(String                  Path,
                                                       ECPrivateKeyParameters  Key)
        {

            File.WriteAllText(Path, new String(PemEncoding.Write("PRIVATE KEY", PrivateKeyInfoFactory.CreatePrivateKeyInfo(Key).GetEncoded())) + "\n");

            // A private key that the whole machine may read is one that the
            // whole machine may sign with. Unix only: Windows inherits the
            // directory's ACL.
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch
                {
                    // A file system with no notion of modes: worth trying, not
                    // worth failing a start over.
                }
            }

            return Key;

        }

        private static ECPrivateKeyParameters ReadKey(String Path)
        {

            var text = File.ReadAllText(Path);

            if (!PemEncoding.TryFind(text, out var fields))
                throw new InvalidOperationException($"'{Path}' holds no PEM.");

            return PrivateKeyFactory.CreateKey(Convert.FromBase64String(text[fields.Base64Data])) as ECPrivateKeyParameters
                       ?? throw new InvalidOperationException($"'{Path}' holds no elliptic curve private key.");

        }

        #endregion


        #region (override) ToString()

        /// <summary>
        /// Which grid operator this is, and its keys.
        /// </summary>
        public override String ToString()

            => $"the grid operator '{Id}', signing key {SigningKey.Id[..16]}…, identity key {IdentityKey.Id[..16]}…";

        #endregion

    }

}
