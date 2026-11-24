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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.GridOperator.Configuration;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// Where the local controllers out there reach this grid operator, where
    /// that is not where it listens: see <see cref="PublicURLsConfiguration"/>.
    /// </summary>
    public partial class GridOperator
    {

        #region PublicURLs

        /// <summary>
        /// The "publicURLs" section of the configuration file, as it was when
        /// the grid operator started: what is said there is what a local
        /// controller is told, whoever asked and however.
        /// </summary>
        public PublicURLsConfiguration PublicURLs { get; private set; } = new ();

        #endregion


        #region (private) ReadThePublicURLs()

        /// <summary>
        /// The "publicURLs" section of the configuration file, as it is when the
        /// grid operator starts.
        /// </summary>
        /// <exception cref="InvalidOperationException">When the section says something that is no URL of its kind.</exception>
        private PublicURLsConfiguration ReadThePublicURLs()
        {

            if (!ConfigFile.TryLoadDocument(out var document, out _) ||
                document[PublicURLsConfiguration.SectionName] is not JToken section ||
                section.Type == JTokenType.Null)
                return new PublicURLsConfiguration();

            if (section is not JObject json)
                throw new InvalidOperationException($"'{ConfigFile.Path}': '{PublicURLsConfiguration.SectionName}' must be an object: where the local controllers reach this grid operator.");

            if (!PublicURLsConfiguration.TryParse(json, out var configuration, out var error))
                throw new InvalidOperationException($"'{ConfigFile.Path}': {error}");

            if (configuration.OpenADR is not null || configuration.WebSocket is not null || configuration.SSH is not null)
                Log.Info($"The local controllers are told {configuration}.", "openadr", "ven");

            return configuration;

        }

        #endregion

        #region (private) VENWebSocketURL(Request, Login) / VENSSHURL(Request, Login)

        /// <summary>
        /// Where a local controller connects to over the WebSocket port of
        /// OpenADR under the given login: below the public URL of the port, where
        /// one is said - else at the name it was reached by, on the port the
        /// server listens on.
        /// </summary>
        private String VENWebSocketURL(HTTPRequest? Request, String Login)

            => PublicURLs.WebSocket is String publicURL
                   ? $"{publicURL}{Uri.EscapeDataString(Login)}"
                   : $"{(VENServerTLS ? "wss" : "ws")}://{PublicHostOf(Request)}:{(VENServerSettings.TCPPort ?? VENServerConfiguration.DefaultTCPPort).ToUInt16()}/{Uri.EscapeDataString(Login)}";

        /// <summary>
        /// Where a local controller connects to over SSH under the given login:
        /// at the public URL of SSH, where one is said - else at the name it was
        /// reached by, on the port the SSH server listens on.
        /// </summary>
        private String VENSSHURL(HTTPRequest? Request, String Login)

            => PublicURLs.SSH is String publicURL
                   ? publicURL.Replace("ssh://", $"ssh://{Uri.EscapeDataString(Login)}@", StringComparison.Ordinal)
                   : $"ssh://{Uri.EscapeDataString(Login)}@{PublicHostOf(Request)}:{SSHPort!.Value.ToUInt16()}";

        #endregion

    }

}
