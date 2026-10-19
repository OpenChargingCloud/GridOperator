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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

using cloud.charging.open.GridOperator.Configuration;
using cloud.charging.open.GridOperator.POI;

using POIRoamingNetwork = cloud.charging.open.protocols.WWCP.POI.RoamingNetwork;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// What came of asking the roaming network: what a publication would
    /// change, or what it changed.
    /// </summary>
    /// <param name="Success">Whether the roaming network could be asked, and - for a publication - took it.</param>
    /// <param name="Message">What happened, in a sentence.</param>
    /// <param name="Refusal">Why not, where not.</param>
    /// <param name="Plan">What it takes, where the head could be read.</param>
    /// <param name="Revision">The revision of the roaming network now.</param>
    /// <param name="Answer">What the roaming network answered to the commit.</param>
    public sealed record PublicationResult(Boolean           Success,
                                           String            Message,
                                           ChangeRefusal?    Refusal    = null,
                                           PublicationPlan?  Plan       = null,
                                           Int64?            Revision   = null,
                                           JObject?          Answer     = null)
    {

        /// <summary>
        /// The result, as the JSON API says it.
        /// </summary>
        public JObject ToJSON(String URL)

            => new (new JProperty("url",       URL),
                    new JProperty("success",   Success),
                    new JProperty("message",   Message),
                    new JProperty("isParty",   Plan?.IsParty),
                    new JProperty("revision",  Revision),
                    new JProperty("changes",   Plan?.Changes.Count ?? 0),
                    new JProperty("steps",     new JArray((Plan?.Steps ?? []).Select(step => step.ToJSON()))),
                    new JProperty("answer",    Answer));

    }


    public partial class GridOperator
    {

        #region Data

        private readonly Lock                    roamingNetworkLock  = new ();
        private          RoamingNetworkConfiguration  roamingNetworkSettings;

        /// <summary>
        /// How long the roaming network is waited for.
        /// </summary>
        public static readonly TimeSpan  RoamingNetworkTimeout  = TimeSpan.FromSeconds(20);

        #endregion

        #region Properties

        /// <summary>
        /// The roaming network this grid operator publishes itself to.
        /// </summary>
        public RoamingNetworkConfiguration  RoamingNetworkSettings
        {
            get
            {
                lock (roamingNetworkLock)
                    return roamingNetworkSettings;
            }
        }

        #endregion


        #region (private) LoadRoamingNetworkSettings()

        /// <summary>
        /// The "roamingNetwork" section the configuration file had at the start
        /// - or none, said in the log, where it was not one.
        /// </summary>
        private RoamingNetworkConfiguration LoadRoamingNetworkSettings()
        {

            if (RoamingNetworkConfiguration.FromDocument(ConfigurationDocument, out var configuration, out var problem))
            {

                Log.Info(configuration.IsSet
                             ? $"The grid operator '{Own.Id}' publishes itself to the roaming network at '{configuration.URL}'."
                             : $"The grid operator '{Own.Id}' publishes itself to no roaming network: none is configured.",
                         "poi", "configuration");

                return configuration;

            }

            Log.Warning($"The roaming network's section of the configuration file was not taken, and this grid operator publishes nowhere: {problem}", "poi", "configuration");

            return RoamingNetworkConfiguration.Default;

        }

        #endregion

        #region TryUpdateRoamingNetworkSettings(JSON, Who, out Error, out NotSaved)

        /// <summary>
        /// Point the publication at another roaming network - or at none.
        /// </summary>
        public Boolean TryUpdateRoamingNetworkSettings(JObject                           JSON,
                                                       String                            Who,
                                                       [NotNullWhen(false)] out String?  Error,
                                                       out Boolean                       NotSaved)
        {

            NotSaved = false;

            if (!RoamingNetworkConfiguration.TryParse(JSON, out var configuration, out Error))
                return false;

            lock (roamingNetworkLock)
            {

                if (!ConfigFile.TryMergeSection(RoamingNetworkConfiguration.SectionName, configuration.ToJSON(), out Error))
                {
                    NotSaved = true;
                    return false;
                }

                var before = roamingNetworkSettings;

                roamingNetworkSettings = configuration;

                if (before.URL != configuration.URL)
                    Log.Notice(configuration.IsSet
                                   ? $"'{Who}' set the roaming network the grid operator '{Own.Id}' publishes itself to to '{configuration.URL}'."
                                   : $"'{Who}' removed the roaming network the grid operator '{Own.Id}' published itself to.",
                               "poi", "configuration", "web");

                return true;

            }

        }

        #endregion

        #region RoamingNetworkSettingsJSON()

        /// <summary>
        /// Where the grid operator publishes itself to, as Configuration →
        /// Roaming network shows it.
        /// </summary>
        public JObject RoamingNetworkSettingsJSON()

            => new (new JProperty("url",           RoamingNetworkSettings.URL),
                    new JProperty("maxURLLength",  RoamingNetworkConfiguration.MaxURLLength),
                    new JProperty("gridOperatorId", Own.Id.ToString()),
                    new JProperty("file",          ConfigFile.Path));

        #endregion


        #region PreviewPublication(CancellationToken)

        /// <summary>
        /// What publishing would change in the roaming network, as its head is
        /// now - nothing signed, nothing sent.
        /// </summary>
        public async Task<PublicationResult> PreviewPublication(CancellationToken CancellationToken = default)
        {

            var (head, failed) = await ReadHead(CancellationToken);

            if (head is null)
                return failed!;

            var plan = PublicationPlan.Of(head.Value.Network, Own.Entity);

            return new PublicationResult(true,
                                         !plan.IsParty  ? NotAPartyYet(head.Value.Network)
                                       : plan.IsEmpty   ? $"The roaming network '{head.Value.Network.Id}' says of the grid operator '{Own.Id}' what this node says: nothing to publish."
                                       :                  $"Publishing makes {plan.Changes.Count} change(s) in the roaming network '{head.Value.Network.Id}', signed by '{Own.Id}'.",
                                         Plan:      plan,
                                         Revision:  head.Value.Revision);

        }

        #endregion

        #region Publish(Who, CancellationToken)

        /// <summary>
        /// Make the grid operator of the roaming network what this node says
        /// it is: one change set of its head, signed with the signing key of
        /// this grid operator, committed to the roaming network.
        /// </summary>
        public async Task<PublicationResult> Publish(String             Who,
                                                     CancellationToken  CancellationToken = default)
        {

            var (head, failed) = await ReadHead(CancellationToken);

            if (head is null)
                return failed!;

            var network  = head.Value.Network;
            var plan     = PublicationPlan.Of(network, Own.Entity);

            if (!plan.IsParty)
                return new PublicationResult(false, NotAPartyYet(network), ChangeRefusal.Conflict, plan, head.Value.Revision);

            if (plan.IsEmpty)
                return new PublicationResult(true,
                                             $"The roaming network '{network.Id}' says of the grid operator '{Own.Id}' what this node says: nothing was published.",
                                             Plan:      plan,
                                             Revision:  head.Value.Revision);

            RoamingNetworkChangeSet changeSet;

            try
            {
                changeSet = Own.Sign(network.CreateChangeSet(plan.Changes,
                                                             Description: new ImmutableI18NString(I18NString.Create(Languages.en,
                                                                              $"The grid operator '{Own.Id}' and its grid connection points, as it says they are."))));
            }
            catch (Exception e)
            {
                return new PublicationResult(false, $"No change set could be made of the roaming network '{network.Id}': {e.Message}", ChangeRefusal.Invalid, plan, head.Value.Revision);
            }

            var settings = RoamingNetworkSettings;

            try
            {

                var (http, path) = ClientFor(settings.URL);

                await using (http)
                {

                    var response = await http.COMMIT(path + "history/changeSets",
                                                     Content:         changeSet.ToJSON().ToString().ToUTF8Bytes(),
                                                     ContentType:     HTTPContentType.Application.JSON_UTF8,
                                                     Accept:          AcceptTypes.FromHTTPContentTypes(HTTPContentType.Application.JSON_UTF8),
                                                     RequestTimeout:  RoamingNetworkTimeout);

                    var answer = TryParseObject(response.HTTPBodyAsUTF8String);

                    if (!response.HTTPStatusCode.IsSuccessful)
                    {

                        Log.Warning($"'{Who}' published the grid operator '{Own.Id}' to '{settings.URL}', which refused it: {response.HTTPStatusCode} {answer?.ToString(Newtonsoft.Json.Formatting.None) ?? ""}",
                                    "poi", "publication");

                        return new PublicationResult(false,
                                                     $"The roaming network '{network.Id}' refused the change set: {response.HTTPStatusCode}" +
                                                     ((answer?["description"] ?? answer?["error"] ?? answer?["message"]) is JToken why ? $", {why}" : "."),
                                                     ChangeRefusal.Upstream,
                                                     plan,
                                                     head.Value.Revision,
                                                     answer);

                    }

                    var revision = answer?["revision"]?.Type == JTokenType.Integer ? answer["revision"]!.Value<Int64>() : (Int64?) null;

                    Log.Notice($"'{Who}' published the grid operator '{Own.Id}' to the roaming network '{network.Id}' at '{settings.URL}': " +
                               $"{plan.Changes.Count} change(s), {answer?["status"] ?? response.HTTPStatusCode.ToString()}" +
                               (revision.HasValue ? $", revision {revision}." : "."),
                               "poi", "publication", "web");

                    return new PublicationResult(true,
                                                 $"The roaming network '{network.Id}' took {plan.Changes.Count} change(s) signed by '{Own.Id}'" +
                                                 (revision.HasValue ? $": it is at revision {revision} now." : "."),
                                                 Plan:      plan,
                                                 Revision:  revision ?? head.Value.Revision,
                                                 Answer:    answer);

                }

            }
            catch (Exception e)
            {
                return new PublicationResult(false, $"The roaming network at '{settings.URL}' could not be reached: {e.Message}", ChangeRefusal.Upstream, plan, head.Value.Revision);
            }

        }

        #endregion


        #region (private) ReadHead(CancellationToken)

        /// <summary>
        /// The head of the roaming network, as its hub serves it - or why not.
        /// </summary>
        private async Task<((POIRoamingNetwork Network, Int64? Revision)? Head, PublicationResult? Failed)> ReadHead(CancellationToken CancellationToken)
        {

            var settings = RoamingNetworkSettings;

            if (!settings.IsSet)
                return (null, new PublicationResult(false, "No roaming network is configured to publish to: Configuration → Roaming network says where it is.", ChangeRefusal.Conflict));

            try
            {

                var (http, path) = ClientFor(settings.URL);

                await using (http)
                {

                    // JSON asked for: a hub answers a request that does not
                    // say what it takes with the page of its web interface.
                    var response = await http.GET(path,
                                                  Accept:          AcceptTypes.FromHTTPContentTypes(HTTPContentType.Application.JSON_UTF8),
                                                  RequestTimeout:  RoamingNetworkTimeout);

                    if (!response.HTTPStatusCode.IsSuccessful)
                        return (null, new PublicationResult(false, $"The roaming network at '{settings.URL}' answered {response.HTTPStatusCode}.", ChangeRefusal.Upstream));

                    var json = TryParseObject(response.HTTPBodyAsUTF8String);

                    if (json is null)
                        return (null, new PublicationResult(false, $"What '{settings.URL}' answered is no JSON object, and so no roaming network.", ChangeRefusal.Upstream));

                    if (!POIRoamingNetwork.TryParse(json, out var network, out var error))
                        return (null, new PublicationResult(false, $"What '{settings.URL}' answered is no roaming network: {error}", ChangeRefusal.Upstream));

                    var revision = json["revision"]?.Type == JTokenType.Integer
                                       ? json["revision"]!.Value<Int64>()
                                       : (Int64?) null;

                    return ((network, revision), null);

                }

            }
            catch (Exception e)
            {
                return (null, new PublicationResult(false, $"The roaming network at '{settings.URL}' could not be reached: {e.Message}", ChangeRefusal.Upstream));
            }

        }

        #endregion

        #region (private) ClientFor(URL)

        /// <summary>
        /// An HTTP client of the server the given URL is on, and the path of the
        /// roaming network on it, with a slash at its end.
        /// </summary>
        private (HTTPClient Client, HTTPPath Path) ClientFor(String URL)
        {

            var uri     = new Uri(URL);
            var server  = org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.Parse($"{uri.Scheme}://{uri.Authority}");
            var path    = HTTPPath.Parse(uri.AbsolutePath.TrimEnd('/') + "/");

            return (new HTTPClient(server, DNSClient: DNSClient), path);

        }

        #endregion

        #region (private) NotAPartyYet(Network)

        private String NotAPartyYet(POIRoamingNetwork Network)

            => $"The roaming network '{Network.Id}' has no grid operator '{Own.Id}' yet: its operator adds it first, with the public halves of its keys - " +
                "Grid operator → Party has them, to hand over.";

        #endregion

        #region (private static) TryParseObject(Text)

        private static JObject? TryParseObject(String? Text)
        {
            try
            {
                return String.IsNullOrWhiteSpace(Text) ? null : JObject.Parse(Text);
            }
            catch
            {
                return null;
            }
        }

        #endregion

    }

}
