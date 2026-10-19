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

#endregion

namespace cloud.charging.open.GridOperator.Configuration
{

    /// <summary>
    /// The roaming network the grid operator publishes itself to: the
    /// "roamingNetwork" section of the configuration file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One URL, of the roaming network as a roaming hub serves it - e.g.
    /// "http://127.0.0.1:2356/poi/RNs/QA". Its head is read from there, and a
    /// change set this grid operator signs goes to "history/changeSets" below
    /// it, by the method COMMIT. Empty is the default, and means that it
    /// publishes nowhere.
    /// </para>
    /// <para>
    /// <code>
    /// "roamingNetwork": { "url": "http://127.0.0.1:2356/poi/RNs/QA" }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="URL">The roaming network: an absolute http or https URL, or empty for none.</param>
    public sealed record RoamingNetworkConfiguration(String URL)
    {

        #region Data

        /// <summary>
        /// The name of the section in the configuration file.
        /// </summary>
        public const String  SectionName   = "roamingNetwork";

        /// <summary>
        /// The longest URL taken.
        /// </summary>
        public const Int32   MaxURLLength  = 2048;

        /// <summary>
        /// What a grid operator starts with: no roaming network.
        /// </summary>
        public static readonly RoamingNetworkConfiguration Default = new ("");

        #endregion

        #region Properties

        /// <summary>
        /// Whether there is a roaming network to publish to.
        /// </summary>
        public Boolean  IsSet
            => URL.Length > 0;

        #endregion


        #region FromDocument(Document, out Configuration, out Error)

        /// <summary>
        /// The "roamingNetwork" section of the whole configuration document -
        /// or the default, where it has none.
        /// </summary>
        public static Boolean FromDocument(JObject                                                Document,
                                           [NotNullWhen(true)]  out RoamingNetworkConfiguration?  Configuration,
                                           [NotNullWhen(false)] out String?                       Error)
        {

            if (!Document.TryGetValue(SectionName, out var section) || section.Type == JTokenType.Null)
            {
                Configuration  = Default;
                Error          = null;
                return true;
            }

            if (section is not JObject json)
            {
                Configuration  = null;
                Error          = $"The \"{SectionName}\" section of the configuration file is no JSON object.";
                return false;
            }

            return TryParse(json, out Configuration, out Error);

        }

        #endregion

        #region TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// A "roamingNetwork" section, or what a page sent in its shape.
        /// </summary>
        public static Boolean TryParse(JObject                                                JSON,
                                       [NotNullWhen(true)]  out RoamingNetworkConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?                       Error)
        {

            Configuration = null;

            var token = JSON["url"];

            if (token is null || token.Type == JTokenType.Null)
            {
                Configuration  = Default;
                Error          = null;
                return true;
            }

            if (token.Type != JTokenType.String)
            {
                Error = "'url' is a URL in quotes, or empty.";
                return false;
            }

            // Without the slash at its end: "history/changeSets" is put below it.
            var url = token.Value<String>()!.Trim().TrimEnd('/');

            if (!IsAcceptable(url, out Error))
                return false;

            Configuration = new RoamingNetworkConfiguration(url);
            return true;

        }

        #endregion

        #region IsAcceptable(URL, out Error)

        /// <summary>
        /// Whether a URL may be where the roaming network is: empty, or an
        /// absolute http or https URL of a sensible length, with a path.
        /// </summary>
        public static Boolean IsAcceptable(String                            URL,
                                           [NotNullWhen(false)] out String?  Error)
        {

            Error = null;

            if (URL.Length == 0)
                return true;

            if (URL.Length > MaxURLLength)
                Error = $"The URL is {URL.Length} characters long; {MaxURLLength} are the most taken.";

            else if (!Uri.TryCreate(URL, UriKind.Absolute, out var uri))
                Error = $"'{URL}' is no absolute URL.";

            else if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                Error = $"'{URL}' is no http or https URL.";

            else if (String.IsNullOrEmpty(uri.Host))
                Error = $"'{URL}' names no host.";

            else if (uri.AbsolutePath.Trim('/').Length == 0)
                Error = $"'{URL}' names no roaming network: it is the hub's, e.g. 'http://127.0.0.1:2356/poi/RNs/QA'.";

            else if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
                Error = $"'{URL}' has a query or a fragment, which a roaming network has not.";

            return Error is null;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section, as the configuration file keeps it.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("url", URL));

        #endregion

    }

}
