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

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.Logins;

#endregion

namespace cloud.charging.open.GridOperator.Configuration
{

    /// <summary>
    /// The "webSocketServer" section of the configuration file of a grid
    /// operator: the WebSocket server the local controllers on its grid - its
    /// VENs - connect to, to speak OpenADR there rather than over HTTPS, and
    /// whether they may connect over the SSH subsystems of OpenADR as well.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The section of the same name of an energy meter, with OpenADR where that
    /// has Modbus: whether the server listens, where, and how a client may say
    /// who it is - by HTTP authentication without TLS or over it, or by its TLS
    /// client certificate. Who it is, is its WebSocket login; which VEN, the
    /// connection this grid operator wrote down for that login.
    /// </para>
    /// <para>
    /// <b>Switched off unless somebody says otherwise</b>: it opens a port to a
    /// network, and the VTN over HTTPS does not need it.
    /// </para>
    /// </remarks>
    /// <param name="Enabled">Whether this grid operator listens for WebSockets of OpenADR at all.</param>
    /// <param name="Address">Which interface it listens on; every one unless said otherwise.</param>
    /// <param name="TCPPort">Which TCP port it listens on.</param>
    /// <param name="ClientAuthentication">How a client may authenticate: by HTTP authentication without TLS or over it, or by its TLS client certificate.</param>
    /// <param name="CheckCertificateRevocation">Whether a client certificate is checked against its issuer's revocation list.</param>
    /// <param name="SSH">Whether a local controller may connect over the SSH subsystems of OpenADR of this grid operator's SSH server as well.</param>
    public sealed record VENServerConfiguration(Boolean?                                       Enabled                      = null,
                                                        IPAddress?                                     Address                      = null,
                                                        IPPort?                                        TCPPort                      = null,
                                                        IReadOnlyList<WebSocketClientAuthentication>?  ClientAuthentication         = null,
                                                        Boolean?                                       CheckCertificateRevocation   = null,
                                                        Boolean?                                       SSH                          = null)
    {

        #region Data

        /// <summary>
        /// The name of this section in the configuration file.
        /// </summary>
        public const String  SectionName   = "webSocketServer";

        /// <summary>
        /// The name of the switch of OpenADR over SSH within it: "ssh": { "enabled": true }.
        /// </summary>
        public const String  SSHFieldName  = "ssh";

        /// <summary>
        /// The TCP port a local controller connects to, unless another is given.
        /// </summary>
        /// <remarks>
        /// Beyond the ports the other OpenChargingCloud boxes use - a smartphone
        /// 2346, a vehicle 2347, a station 2348 to 2350, a CSMS 2351 and 2352, a
        /// gateway 2353 and 2354, an EMSP 2355 and 2359, a roaming hub 2356, an
        /// energy meter 2357, the web interface of this grid operator 2358, a
        /// PKI 2360, a time server 2361 and a name server 2362 - which are tried
        /// out on the same bench: the next one nobody has.
        /// </remarks>
        public static readonly IPPort                           DefaultTCPPort               = IPPort.Parse(2363);

        /// <summary>
        /// How a client may authenticate where nothing says otherwise: every way.
        /// </summary>
        public static readonly WebSocketClientAuthentication[]  DefaultClientAuthentication  = WebSocketClientAuthentications.All;

        #endregion

        #region Properties

        /// <summary>
        /// Whether this grid operator listens for WebSockets of OpenADR.
        /// </summary>
        public Boolean IsEnabled
            => Enabled == true;

        /// <summary>
        /// Whether OpenADR may come over SSH as well.
        /// </summary>
        public Boolean TakesSSH
            => SSH == true;

        #endregion


        #region (static) TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// The section, or the one sentence that says what is wrong with it.
        /// </summary>
        public static Boolean TryParse(JObject                                                  JSON,
                                       [NotNullWhen(true)]  out VENServerConfiguration?          Configuration,
                                       [NotNullWhen(false)] out String?                          Error)
        {

            Configuration = null;

            if (!ConfigurationReader.TryReadBoolean(JSON, "enabled",                     SectionName, out var enabled,      out Error) ||
                !ConfigurationReader.TryReadPort   (JSON, "port",                        SectionName, out var port,         out Error) ||
                !ConfigurationReader.TryReadBoolean(JSON, "checkCertificateRevocation",  SectionName, out var checkCRL,     out Error) ||
                !ConfigurationReader.TryReadString (JSON, "address",                     SectionName, 45, out var addressText, out Error))
            {
                return false;
            }

            IPAddress? address = null;

            if (addressText is not null && (address = IPAddress.TryParse(addressText)) is null)
            {
                Error = $"'{SectionName}.address' must be an IP address, e.g. \"0.0.0.0\" for every interface.";
                return false;
            }

            IReadOnlyList<WebSocketClientAuthentication>? clientAuthentication = null;

            if (JSON["clientAuthentication"] is JToken authenticationToken && authenticationToken.Type != JTokenType.Null)
            {

                var ways = $"{String.Join(", ", WebSocketClientAuthentications.Names.Select(name => $"\"{name}\""))}";

                if (authenticationToken is not JArray authenticationArray)
                {
                    Error = $"'{SectionName}.clientAuthentication' must be an array of the ways a client may authenticate: {ways}.";
                    return false;
                }

                var authentications = new List<WebSocketClientAuthentication>();

                foreach (var token in authenticationArray)
                {

                    if (token.Type != JTokenType.String ||
                        !WebSocketClientAuthentications.TryParse(token.Value<String>(), out var authentication))
                    {
                        Error = $"'{SectionName}.clientAuthentication': {token.ToString(Newtonsoft.Json.Formatting.None)} is no way a client may authenticate; they are {ways}.";
                        return false;
                    }

                    if (!authentications.Contains(authentication))
                        authentications.Add(authentication);

                }

                if (authentications.Count == 0)
                {
                    Error = $"'{SectionName}.clientAuthentication' must allow at least one way, or nothing can connect at all. Switch the server off instead.";
                    return false;
                }

                clientAuthentication = [.. authentications.Order()];

            }

            Boolean? ssh = null;

            if (JSON[SSHFieldName] is JToken sshToken && sshToken.Type != JTokenType.Null)
            {

                if (sshToken is not JObject sshJSON)
                {
                    Error = $"'{SectionName}.{SSHFieldName}' must be a JSON object, e.g. {{ \"enabled\": true }}.";
                    return false;
                }

                if (!ConfigurationReader.TryReadBoolean(sshJSON, "enabled", $"{SectionName}.{SSHFieldName}", out ssh, out Error))
                    return false;

            }

            Configuration = new VENServerConfiguration(
                                enabled,
                                address,
                                port,
                                clientAuthentication,
                                checkCRL,
                                ssh
                            );

            Error = null;
            return true;

        }

        #endregion

        #region Effective()

        /// <summary>
        /// This section with every field filled in.
        /// </summary>
        public VENServerConfiguration Effective()

            => new (
                   Enabled                    ?? false,
                   Address                    ?? IPv4Address.Any,
                   TCPPort                    ?? DefaultTCPPort,
                   ClientAuthentication       ?? DefaultClientAuthentication,
                   CheckCertificateRevocation ?? false,
                   SSH                        ?? false
               );

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section as it is written to the file: what it says, and nothing
        /// it was not told.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject();

            if (Enabled.HasValue)                     json.Add("enabled",                     Enabled.Value);
            if (Address is not null)                  json.Add("address",                     Address.ToString());
            if (TCPPort.HasValue)                     json.Add("port",                        TCPPort.Value.ToUInt16());
            if (ClientAuthentication is not null)     json.Add("clientAuthentication",        new JArray(ClientAuthentication.Select(authentication => authentication.ToText())));
            if (CheckCertificateRevocation.HasValue)  json.Add("checkCertificateRevocation",  CheckCertificateRevocation.Value);
            if (SSH.HasValue)                         json.Add(SSHFieldName,                  new JObject(new JProperty("enabled", SSH.Value)));

            return json;

        }

        #endregion

        #region (override) ToString()

        public override String ToString()

            => !IsEnabled
                   ? "WebSocket server of OpenADR off"
                   : $"WebSocket server of OpenADR on {Address?.ToString() ?? "every interface"}" +
                     (TCPPort.HasValue ? $":{TCPPort}"           : "") +
                     (TakesSSH         ? ", over SSH as well"    : "");

        #endregion

    }

}
