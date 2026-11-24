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
    /// The "publicURLs" section of the configuration file of a grid operator:
    /// where the local controllers out there reach it, where that is not where
    /// it listens - behind a reverse proxy that puts it below a path of its
    /// own, on another port or under another name, or behind a port forward of
    /// SSH.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the grid operator tells a local controller - the URL of its VTN
    /// and of its token, the URL of its connection over the WebSocket port or
    /// over SSH - it otherwise makes of the name the one asking reached it by,
    /// as a trusted proxy says it or as the request's own Host says it, and of
    /// the path and the ports it has itself. A proxy that serves it at
    /// https://grid.example/dso/ while it listens on 127.0.0.1:2358/, that
    /// takes WebSockets on 443 for a server on 2363, or a firewall that
    /// forwards 2222 to its SSH port, makes each of those wrong; what is said
    /// here is said instead.
    /// </para>
    /// <para>
    /// Each is said on its own, and what is not said is made as before.
    /// </para>
    /// </remarks>
    /// <param name="OpenADR">The URL of the VTN over HTTP(S), e.g. "https://grid.example/dso/openadr3/"; its token is "auth/token" below it.</param>
    /// <param name="WebSocket">The URL the WebSocket port of OpenADR is reached at, e.g. "wss://grid.example/dso/openadr-ws/"; the login of a connection is the last segment of its path.</param>
    /// <param name="SSH">Where the SSH server is reached, e.g. "ssh://grid.example:2222"; the login of a connection is its user.</param>
    public sealed record PublicURLsConfiguration(String?  OpenADR    = null,
                                                 String?  WebSocket  = null,
                                                 String?  SSH        = null)
    {

        #region Data

        /// <summary>
        /// The name of this section in the configuration file.
        /// </summary>
        public const String  SectionName   = "publicURLs";

        /// <summary>
        /// The longest URL taken.
        /// </summary>
        public const Int32   MaxURLLength  = 2048;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The section, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                              JSON,
                                       [NotNullWhen(true)]  out PublicURLsConfiguration?    Configuration,
                                       [NotNullWhen(false)] out String?                     Error)
        {

            Configuration = null;

            if (!TryReadURL(JSON, "openADR",   [ "http", "https" ], "\"https://grid.example/dso/openadr3/\"", WithPath: true,  out var openADR,   out Error) ||
                !TryReadURL(JSON, "webSocket", [ "ws",   "wss"   ], "\"wss://grid.example/dso/openadr-ws/\"", WithPath: true,  out var webSocket, out Error) ||
                !TryReadURL(JSON, "ssh",       [ "ssh"           ], "\"ssh://grid.example:2222\"",             WithPath: false, out var ssh,       out Error))
            {
                return false;
            }

            Configuration = new PublicURLsConfiguration(openADR, webSocket, ssh);
            return true;

        }

        #endregion

        #region (private static) TryReadURL(JSON, Name, Schemes, Example, WithPath, out URL, out Error)

        /// <summary>
        /// One URL of the section: absent, or an absolute URL of one of the given
        /// schemes, with a host, without a user, a query or a fragment - and
        /// without a path where none is wanted. One with a path ends with a
        /// slash, so that what comes below it is added.
        /// </summary>
        private static Boolean TryReadURL(JObject                           JSON,
                                          String                            Name,
                                          String[]                          Schemes,
                                          String                            Example,
                                          Boolean                           WithPath,
                                          out String?                       URL,
                                          [NotNullWhen(false)] out String?  Error)
        {

            URL    = null;
            Error  = null;

            if (JSON[Name] is not JToken token || token.Type == JTokenType.Null)
                return true;

            var where = $"'{SectionName}.{Name}'";

            if (token.Type != JTokenType.String ||
                token.Value<String>()?.Trim() is not { Length: > 0 } text)
            {
                Error = $"{where} must be a URL, e.g. {Example}.";
                return false;
            }

            if (text.Length > MaxURLLength)
            {
                Error = $"{where} is {text.Length} characters long; {MaxURLLength} are the most taken.";
                return false;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                !Schemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase) ||
                uri.Host.Length == 0)
            {
                Error = $"{where} must be a {String.Join(" or ", Schemes.Select(scheme => $"{scheme}://"))} URL with a host, e.g. {Example}; '{text}' is not.";
                return false;
            }

            if (uri.UserInfo.Length > 0)
            {
                Error = $"{where} names no user: the login of each local controller is put into it.";
                return false;
            }

            if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
            {
                Error = $"{where} has no query and no fragment, e.g. {Example}.";
                return false;
            }

            if (!WithPath)
            {

                if (uri.AbsolutePath != "/")
                {
                    Error = $"{where} has no path: a name and a port, e.g. {Example}.";
                    return false;
                }

                URL = $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority}";
                return true;

            }

            URL = $"{uri.Scheme.ToLowerInvariant()}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}/";
            return true;

        }

        #endregion


        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file: what it says, and nothing
        /// it was not told.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (OpenADR   is not null)  json.Add("openADR",    OpenADR);
            if (WebSocket is not null)  json.Add("webSocket",  WebSocket);
            if (SSH       is not null)  json.Add("ssh",        SSH);

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()
        {

            var said = new List<String>();

            if (OpenADR   is not null)  said.Add($"the VTN at {OpenADR}");
            if (WebSocket is not null)  said.Add($"the WebSocket port at {WebSocket}");
            if (SSH       is not null)  said.Add($"SSH at {SSH}");

            return said.Count == 0
                       ? "no public URLs: each is made of the request"
                       : $"public URLs: {String.Join(", ", said)}";

        }

        #endregion

    }

}
