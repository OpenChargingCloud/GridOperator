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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Logins;
using cloud.charging.open.protocols.WWCP.Node.Connections;

using cloud.charging.open.GridOperator.OpenADR;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// A connection made, for a local controller to connect over: what was
    /// written down, the login it comes under, where it connects to, and the
    /// password it signs in with - the one time it is said - where one was made.
    /// </summary>
    /// <param name="Connection">The connection, as it was written down.</param>
    /// <param name="Login">The login the local controller comes under.</param>
    /// <param name="URL">Where it connects to: what it has as its "vtnURL", and as the URL of its own connection to this grid operator.</param>
    /// <param name="Password">The password made for it, or null where it signs in with an SSH key.</param>
    /// <param name="SSHHostKey">Over SSH: the fingerprint of the host key of this grid operator, which the local controller pins.</param>
    public sealed record VENConnectionMade(ConnectionEntry  Connection,
                                           String           Login,
                                           String           URL,
                                           String?          Password,
                                           String?          SSHHostKey);


    public partial class GridOperator
    {

        #region Data

        /// <summary>
        /// The ways a local controller may connect, as they are asked for.
        /// </summary>
        public static readonly String[] VENTransports  = [ "ws", "ssh" ];

        /// <summary>
        /// The bindings of OpenADR a connection may speak, as they are asked for.
        /// </summary>
        public static readonly String[] VENBindings    = [ "messages", "rest" ];

        #endregion


        #region TryConnectLocalController(VENName, JSON, Who, Request, out Made, out Error, out Refusal)

        /// <summary>
        /// Let a local controller connect over the WebSocket port of OpenADR or
        /// over SSH, rather than over HTTPS: { "transport": "ws" | "ssh",
        /// "binding": "messages" | "rest", "login", "sshKey", "description" }.
        /// A connection of the type VEN is written down for it, and a login it
        /// comes under - with a password made now, said once and kept only as
        /// its hash, or with the SSH key it brought.
        /// </summary>
        /// <remarks>
        /// <para>
        /// "ws" is the WebSocket port, which speaks wss:// where it has TLS;
        /// the binding is "messages" unless said otherwise. The login is the VEN
        /// name, or - where that is somebody's already - the VEN name and the
        /// transport.
        /// </para>
        /// <para>
        /// The URL is at the name the one asking reached this grid operator by,
        /// as for the VTN over HTTPS (see <see cref="OpenADRVTNURLFor"/>), on
        /// the port of the WebSocket server or of the SSH server: one a proxy in
        /// front of the web interface does not put somewhere else.
        /// </para>
        /// </remarks>
        /// <param name="VENName">The local controller.</param>
        /// <param name="JSON">How it connects.</param>
        /// <param name="Who">Who lets it.</param>
        /// <param name="Request">The request that asks, for the name the URL is at; none at the command line.</param>
        /// <param name="Made">The connection made.</param>
        /// <param name="Error">Why none was.</param>
        /// <param name="Refusal">What kind of no it was.</param>
        public Boolean TryConnectLocalController(String                                         VENName,
                                                 JObject                                        JSON,
                                                 String                                         Who,
                                                 HTTPRequest?                                   Request,
                                                 [NotNullWhen(true)]  out VENConnectionMade?   Made,
                                                 [NotNullWhen(false)] out String?              Error,
                                                 out ChangeRefusal                              Refusal)
        {

            Made     = null;
            Refusal  = ChangeRefusal.Invalid;

            #region The local controller, and how it connects

            if (OpenADR.Login(VENName) is not LocalControllerLogin controller)
            {
                Error    = $"There is no local controller '{VENName}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            var transport    = (JSON.Value<String>("transport") ?? "").Trim().ToLowerInvariant();
            var binding      = (JSON.Value<String>("binding")   ?? "messages").Trim().ToLowerInvariant();
            var loginText    = JSON.Value<String>("login")?.      Trim();
            var sshKey       = JSON.Value<String>("sshKey")?.     Trim();
            var description  = JSON.Value<String>("description")?.Trim();

            if (transport == "wss")
                transport = "ws";

            if (!VENTransports.Contains(transport))
            {
                Error = $"'{transport}' is no way a local controller connects: {String.Join(" or ", VENTransports.Select(way => $"'{way}'"))}.";
                return false;
            }

            if (!VENBindings.Contains(binding))
            {
                Error = $"'{binding}' is no binding of OpenADR: {String.Join(" or ", VENBindings.Select(way => $"'{way}'"))}.";
                return false;
            }

            if (String.IsNullOrEmpty(sshKey))
                sshKey = null;

            if (sshKey is not null && transport != "ssh")
            {
                Error = "An SSH key signs in over SSH only: over the WebSocket port a password is made.";
                return false;
            }

            if (description is { Length: > MaxTextLength })
            {
                Error = $"The description is {description.Length} characters long; {MaxTextLength} are the most taken.";
                return false;
            }

            #endregion

            #region Where it connects to: the port has to be there

            var host = PublicHostOf(Request);

            String? hostKey = null;

            if (transport == "ws")
            {

                if (!VENServerSettings.IsEnabled)
                {
                    Error    = $"The WebSocket server of OpenADR is switched off: '{Configuration.VENServerConfiguration.SectionName}.enabled' in the configuration switches it on.";
                    Refusal  = ChangeRefusal.Conflict;
                    return false;
                }

                var needed = VENServerTLS ? WebSocketClientAuthentication.HTTPAuthOverTLS
                                          : WebSocketClientAuthentication.HTTPAuthWithoutTLS;

                if (!(VENServerSettings.ClientAuthentication ?? Configuration.VENServerConfiguration.DefaultClientAuthentication).Contains(needed))
                {
                    Error    = $"The WebSocket server of OpenADR takes no password {(VENServerTLS ? "over TLS" : "without TLS")}: " +
                               $"'{Configuration.VENServerConfiguration.SectionName}.clientAuthentication' would have to allow \"{needed.ToText()}\".";
                    Refusal  = ChangeRefusal.Conflict;
                    return false;
                }

            }
            else
            {

                if (!VENServerSettings.TakesSSH || SSHPort is null)
                {
                    Error    = $"OpenADR over SSH is switched off: the SSH server of this grid operator and '{Configuration.VENServerConfiguration.SectionName}.{Configuration.VENServerConfiguration.SSHFieldName}.enabled' switch it on.";
                    Refusal  = ChangeRefusal.Conflict;
                    return false;
                }

                hostKey = SSHHostKey?.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();

            }

            #endregion

            #region The login it comes under: its own, nobody else's

            User_Id login;

            if (loginText is not null)
            {

                if (User_Id.TryParse(loginText) is not User_Id given || given.ToString() != loginText)
                {
                    Error = $"'{loginText}' is no login.";
                    return false;
                }

                if (LoginIsTaken(given))
                {
                    Error    = $"The login '{given}' is somebody's already.";
                    Refusal  = ChangeRefusal.Conflict;
                    return false;
                }

                login = given;

            }
            else
            {

                var free = new[] { controller.VENName, $"{controller.VENName}-{transport}", $"{controller.VENName}-{transport}-{binding}" }.
                               Select (name => User_Id.TryParse(name)).
                               Where  (name => name is not null).
                               Select (name => name!.Value).
                               Where  (name => !LoginIsTaken(name)).
                               ToArray();

                if (free.Length == 0)
                {
                    Error    = $"Every login a connection of '{controller.VENName}' would come under is somebody's already: give one as \"login\".";
                    Refusal  = ChangeRefusal.Conflict;
                    return false;
                }

                login = free[0];

            }

            var url = transport == "ws"
                      ? $"{(VENServerTLS ? "wss" : "ws")}://{host}:{(VENServerSettings.TCPPort ?? Configuration.VENServerConfiguration.DefaultTCPPort).ToUInt16()}/{Uri.EscapeDataString(login.ToString())}"
                      : $"ssh://{Uri.EscapeDataString(login.ToString())}@{host}:{SSHPort!.Value.ToUInt16()}";

            #endregion

            var connectionDescription  = I18NString.Create(description ?? $"The local controller '{controller.VENName}' over {(transport == "ws" ? "the WebSocket port" : "SSH")}, {binding}");
            var loginDescription       = I18NString.Create($"The local controller '{controller.VENName}'");

            #region The login: a password made now, or the SSH key it brought

            String? password = null;

            if (sshKey is not null)
            {
                if (!WebSocketLogins.TryAddSSHKey(login, sshKey, controller.VENName, null, loginDescription, Who, out _, out Error, out var keyNotSaved))
                {
                    Refusal = keyNotSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                    return false;
                }
            }
            else
            {

                password = NewVENPassword();

                if (!WebSocketLogins.TrySetPasswordAuthentication(login, password, null, loginDescription, out Error, out var passwordNotSaved))
                {
                    Refusal = passwordNotSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                    return false;
                }

            }

            #endregion

            #region The connection, or the login is taken back

            if (!Connections.TryAddConnection(connectionDescription,
                                              URL.Parse(url),
                                              "VEN",
                                              true,
                                              null,
                                              null,
                                              out var id,
                                              out Error,
                                              out var connectionNotSaved,
                                              Inbound:         true,
                                              OpenADRBinding:  binding,
                                              VEN:             controller.VENName))
            {
                WebSocketLogins.TryRemove(login, out _);
                Refusal = connectionNotSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                return false;
            }

            #endregion

            var entry = Connections.Connections.First(connection => connection.Id == id);

            Log.Notice($"'{Who}' let the local controller '{controller.VENName}' connect over {url} in the binding of {binding}, " +
                       (password is null ? "signing in with its SSH key." : "signing in with a password said once."),
                       "openadr", "ven", "connections", "web");

            Made     = new VENConnectionMade(entry, login.ToString(), url, password, hostKey);
            Error    = null;
            Refusal  = ChangeRefusal.Invalid;
            return true;

        }

        #endregion

        #region TryDisconnectLocalController(VENName, ConnectionId, Who, out Error, out Refusal)

        /// <summary>
        /// Take a connection of a local controller away, and the login it came
        /// under where no other connection comes under it: whatever is in over
        /// it now is let go of.
        /// </summary>
        /// <param name="VENName">The local controller.</param>
        /// <param name="ConnectionId">The connection.</param>
        /// <param name="Who">Who takes it away.</param>
        /// <param name="Error">Why it was not.</param>
        /// <param name="Refusal">What kind of no it was.</param>
        public Boolean TryDisconnectLocalController(String                            VENName,
                                                    String                            ConnectionId,
                                                    String                            Who,
                                                    [NotNullWhen(false)] out String?  Error,
                                                    out ChangeRefusal                 Refusal)
        {

            if (OpenADR.Login(VENName) is not LocalControllerLogin controller)
            {
                Error    = $"There is no local controller '{VENName}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            if (VENConnectionsOf(controller.VENName).FirstOrDefault(entry => entry.Id == ConnectionId) is not ConnectionEntry entry)
            {
                Error    = $"The local controller '{controller.VENName}' has no connection '{ConnectionId}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            if (!TryRemoveVENConnection(entry, out Error, out var notSaved))
            {
                Refusal = notSaved ? ChangeRefusal.NotSaved : ChangeRefusal.Invalid;
                return false;
            }

            Log.Notice($"'{Who}' took the connection '{entry.Id}' ({entry.URL}) of the local controller '{controller.VENName}' away.",
                       "openadr", "ven", "connections", "web");

            Refusal = ChangeRefusal.Invalid;
            return true;

        }

        #endregion

        #region VENConnectableJSON()

        /// <summary>
        /// Over which ways a local controller may be let connect now, as the
        /// page of the local controllers offers them: { "ws", "ssh" }.
        /// </summary>
        public JObject VENConnectableJSON()

            => new (
                   new JProperty("ws",   VENServerSettings.IsEnabled),
                   new JProperty("ssh",  VENServerSettings.TakesSSH && SSHPort is not null)
               );

        #endregion

        #region VENConnectionMadeJSON(Made)

        /// <summary>
        /// A connection made, as the JSON API answers: the connection as the
        /// local controller's JSON shows it, and what the local controller
        /// connects with - the one answer that says its password.
        /// </summary>
        /// <remarks>
        /// The "credentials" are named as the local controller has them: its
        /// "vtnURL" in its section of OpenADR, and the URL, "openADRBinding" and
        /// "sshHostKey" of its own connection to this grid operator.
        /// </remarks>
        public JObject VENConnectionMadeJSON(VENConnectionMade Made)
        {

            var json         = VENConnectionJSON(Made.Connection, ConnectionStates.GetValueOrDefault(Made.Connection.Id));
            var credentials  = new JObject(
                                   new JProperty("vtnURL",          Made.URL),
                                   new JProperty("openADRBinding",  ConnectionEntry.AsText(Made.Connection.OpenADRBinding)),
                                   new JProperty("login",           Made.Login)
                               );

            if (Made.Password   is not null) credentials.Add(new JProperty("password",    Made.Password));
            if (Made.SSHHostKey is not null) credentials.Add(new JProperty("sshHostKey",  Made.SSHHostKey));

            json.AddFirst(new JProperty("venName", Made.Connection.VEN));
            json.Add(new JProperty("credentials", credentials));

            return json;

        }

        #endregion

        #region VENConnectionsJSON(VENName)

        /// <summary>
        /// The connections of the given local controller, as its JSON shows them.
        /// </summary>
        public JArray VENConnectionsJSON(String VENName)
        {
            var states = ConnectionStates;
            return new JArray(VENConnectionsOf(VENName).Select(entry => VENConnectionJSON(entry, states.GetValueOrDefault(entry.Id))));
        }

        /// <summary>
        /// The connections of the given local controller, one short text each:
        /// "ssh, messages" - for the command line.
        /// </summary>
        public IReadOnlyList<String> VENConnectionsText(String VENName)

            => [ .. VENConnectionsOf(VENName).Select(entry => $"{(entry.IsSSH ? "ssh" : entry.IsSecure ? "wss" : "ws")}, {ConnectionEntry.AsText(entry.OpenADRBinding)}") ];

        #endregion


        #region (private) TryRemoveVENConnection(Entry, out Error, out NotSaved)

        /// <summary>
        /// Take the given connection of a VEN away, and the login it came under
        /// where no other connection comes under it.
        /// </summary>
        private Boolean TryRemoveVENConnection(ConnectionEntry                   Entry,
                                               [NotNullWhen(false)] out String?  Error,
                                               out Boolean                       NotSaved)
        {

            if (!Connections.TryRemoveConnection(Entry.Id, out Error, out NotSaved))
                return false;

            if (Entry.InboundName is String name &&
                User_Id.TryParse(name) is User_Id login &&
                !Connections.Connections.Any(connection => connection.Inbound &&
                                                           String.Equals(connection.InboundName, name, StringComparison.Ordinal)) &&
                WebSocketLogins.TryGet(login, out _))
            {
                // The connection is gone either way; a login left behind lets in
                // nothing without a connection written down for it.
                if (!WebSocketLogins.TryRemove(login, out var loginError))
                    Log.Warning($"The login '{login}' of the connection '{Entry.Id}' that was taken away stays: {loginError}", "openadr", "ven", "connections");
            }

            Error = null;
            return true;

        }

        #endregion

        #region (private) RemoveVENConnectionsOf(VENName)

        /// <summary>
        /// Take every connection of the given local controller away, and the
        /// logins they came under: when it is removed.
        /// </summary>
        private void RemoveVENConnectionsOf(String VENName)
        {
            foreach (var entry in VENConnectionsOf(VENName).ToArray())
                if (!TryRemoveVENConnection(entry, out var error, out _))
                    Log.Warning($"The connection '{entry.Id}' of the local controller '{VENName}', which was removed, stays: {error}", "openadr", "ven", "connections");
        }

        #endregion

        #region (private) LoginIsTaken(Login)

        /// <summary>
        /// Whether the given login is somebody's already: a login of the
        /// WebSocket port or of SSH, or the name a connection comes under.
        /// </summary>
        private Boolean LoginIsTaken(User_Id Login)

            => WebSocketLogins.TryGet(Login, out _) ||
               Connections.Connections.Any(connection => connection.Inbound &&
                                                         String.Equals(connection.InboundName, Login.ToString(), StringComparison.Ordinal));

        #endregion

        #region (private) PublicHostOf(Request)

        /// <summary>
        /// The name of this grid operator, without a port, that the one who sent
        /// the given request reached it by - as a trusted proxy says it, or its
        /// own Host. Without a request, or with a Host that is no host: where the
        /// web interface is, or - where that is every interface - the name of
        /// this machine.
        /// </summary>
        private String PublicHostOf(HTTPRequest? Request)
        {

            var authority = Request is not null
                                ? (ProxyHostOf(Request) ?? Request.GetHeaderField("Host"))?.Trim()
                                : null;

            if (authority is not null && URLAuthority.IsMatch(authority))
            {

                if (authority.StartsWith('['))
                    return authority[..(authority.IndexOf(']') + 1)];

                var colon = authority.LastIndexOf(':');

                return colon >= 0 ? authority[..colon] : authority;

            }

            var host = new Uri(WebInterfaceURL.ToString()).Host;

            return host is "0.0.0.0" or "[::]"
                       ? Environment.MachineName.ToLowerInvariant()
                       : host;

        }

        #endregion

        #region (private static) NewVENPassword()

        /// <summary>
        /// A password for a local controller to connect with: 32 characters of
        /// base64url, 192 bits.
        /// </summary>
        private static String NewVENPassword()

            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).
                       Replace('+', '-').
                       Replace('/', '_');

        #endregion

    }

}
