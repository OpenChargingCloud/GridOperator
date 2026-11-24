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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    /// <summary>
    /// The local controllers that may sign in at the OpenADR 3 VTN - the VENs
    /// of the charging pools on this grid: all of them, one line each; "add"
    /// one, which says its secret the one time; "secret" makes a new one;
    /// "remove" one; "connect" one over the WebSocket port or over SSH, which
    /// says its password the one time, and "disconnect" such a connection.
    /// </summary>
    public class LocalControllersCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                               ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public static readonly String CommandName = nameof(LocalControllersCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                return new[] { "add", "secret", "remove", "connect", "disconnect" }.
                           Where (word => word.StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} {word}"));

            if (Arguments.Length == 3 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase) &&
                new[] { "secret", "remove", "connect", "disconnect" }.Contains(Arguments[1], StringComparer.OrdinalIgnoreCase))
                return cli.GridOperator.OpenADR.Logins().
                           Select(login => login.VENName).
                           Where (name  => name.StartsWith(Arguments[2], StringComparison.OrdinalIgnoreCase)).
                           Select(name  => SuggestionResponse.ParameterCompleted($"{CommandName} {Arguments[1]} {name}"));

            if (Arguments.Length == 4 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase) &&
                Arguments[1].Equals("connect", StringComparison.OrdinalIgnoreCase))
                return GridOperator.VENTransports.
                           Where (word => word.StartsWith(Arguments[3], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} connect {Arguments[2]} {word}"));

            if (Arguments.Length == 5 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase) &&
                Arguments[1].Equals("connect", StringComparison.OrdinalIgnoreCase))
                return GridOperator.VENBindings.
                           Where (word => word.StartsWith(Arguments[4], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} connect {Arguments[2]} {Arguments[3]} {word}"));

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            var rest = Arguments.Skip(1).ToArray();

            if (rest.Length > 0)
            {

                var what = rest[0].ToLowerInvariant();

                if (what is "add" or "secret" or "remove" or "connect" or "disconnect")
                {

                    if (!cli.MayDo(Permission.Edit(GridOperatorAccess.OpenADR), $"{CommandName} {what}", out var notAllowed))
                        return Task.FromResult<String[]>([ notAllowed ]);

                    return Task.FromResult(what switch {
                        "add"         => Added       (rest[1..]),
                        "secret"      => Renewed     (rest[1..]),
                        "connect"     => Connected   (rest[1..]),
                        "disconnect"  => Disconnected(rest[1..]),
                        _             => Removed     (rest[1..])
                    });

                }

                return Task.FromResult<String[]>([ $"Usage: {Help()}" ]);

            }

            if (!cli.MayDo(Permission.Read(GridOperatorAccess.OpenADR), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var logins = cli.GridOperator.OpenADR.Logins();

            if (logins.Count == 0)
                return Task.FromResult<String[]>([ $"No local controller may sign in at {cli.GridOperator.OpenADRVTNURL} yet: '{CommandName} add <venName> <chargingPoolId> <gridConnectionPointId>' lets one." ]);

            var width = logins.Max(login => login.VENName.Length);

            return Task.FromResult<String[]>([
                       .. logins.Select(login => {
                              var seen         = cli.GridOperator.OpenADR.LastSeen(login.VENName);
                              var connections  = cli.GridOperator.VENConnectionsText(login.VENName);
                              return $"{login.VENName.PadRight(width)}  pool {login.ChargingPoolId}, behind {login.GridConnectionPointId}, " +
                                     (seen.HasValue ? $"last seen {seen.Value.ToLocalTime():HH:mm:ss}" : "not seen since this start") +
                                     (connections.Count > 0 ? $"; connects over {String.Join(" and ", connections.Select(connection => $"({connection})"))}" : "");
                          }),
                       $"They sign in at {cli.GridOperator.OpenADRTokenURL}."
                   ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [add <venName> <chargingPoolId> <gridConnectionPointId> | secret <venName> | remove <venName> | " +
               $"connect <venName> ws|ssh [messages|rest] [<ssh key>] | disconnect <venName> <connectionId>] - the local controllers that may sign in at the OpenADR 3 VTN, and connect to it";

        #endregion


        #region (private) Added(Arguments) / Renewed(Arguments) / Removed(Arguments)

        private String[] Added(String[] Arguments)
        {

            if (Arguments.Length < 3)
                return [ $"Usage: {CommandName} add <venName> <chargingPoolId> <gridConnectionPointId> [<description>]" ];

            var json = new JObject(
                           new JProperty("venName",                Arguments[0]),
                           new JProperty("chargingPoolId",         Arguments[1]),
                           new JProperty("gridConnectionPointId",  Arguments[2])
                       );

            if (Arguments.Length > 3)
                json["description"] = String.Join(" ", Arguments[3..]);

            if (!cli.GridOperator.TryAddLocalController(json, cli.Who, out var login, out var secret, out var error, out _))
                return [ error ];

            return Credentials($"The local controller '{login.VENName}' may sign in now. Its secret is said this once:", login.VENName, secret);

        }

        private String[] Renewed(String[] Arguments)
        {

            if (Arguments.Length != 1)
                return [ $"Usage: {CommandName} secret <venName>" ];

            if (!cli.GridOperator.TryNewSecret(Arguments[0], cli.Who, out var login, out var secret, out var error, out _))
                return [ error ];

            return Credentials($"The local controller '{login.VENName}' has a new secret, said this once; the old one signs in no more:", login.VENName, secret);

        }

        private String[] Removed(String[] Arguments)
        {

            if (Arguments.Length != 1)
                return [ $"Usage: {CommandName} remove <venName>" ];

            return cli.GridOperator.TryRemoveLocalController(Arguments[0], cli.Who, out var error, out _)
                       ? [ $"The local controller '{Arguments[0]}' signs in no more." ]
                       : [ error ];

        }

        /// <summary>
        /// "connect &lt;venName&gt; ws|ssh [messages|rest] [&lt;ssh key&gt;]": the
        /// key, if any, as one line of an authorized_keys file, its spaces and all.
        /// </summary>
        private String[] Connected(String[] Arguments)
        {

            if (Arguments.Length < 2)
                return [ $"Usage: {CommandName} connect <venName> ws|ssh [messages|rest] [<ssh key>]" ];

            var json   = new JObject(new JProperty("transport", Arguments[1]));
            var keyAt  = 2;

            if (Arguments.Length > 2 && GridOperator.VENBindings.Contains(Arguments[2], StringComparer.OrdinalIgnoreCase))
            {
                json["binding"] = Arguments[2];
                keyAt = 3;
            }

            if (Arguments.Length > keyAt)
                json["sshKey"] = String.Join(" ", Arguments[keyAt..]);

            if (!cli.GridOperator.TryConnectLocalController(Arguments[0], json, cli.Who, null, out var made, out var error, out _))
                return [ error ];

            return [
                       $"The local controller '{made.Connection.VEN}' may connect over {made.URL} now, in the binding of {ConnectionEntryBinding(made)} - its connection '{made.Connection.Id}'.",
                       .. made.Password is not null
                              ? new[] { $"  login     {made.Login}",
                                        $"  password  {made.Password}",
                                        "The password is said this once." }
                              : [ $"  login     {made.Login}, with its SSH key" ],
                       .. made.SSHHostKey is not null
                              ? new[] { $"  host key  {made.SSHHostKey}" }
                              : [],
                       "At the local controller this URL is its \"vtnURL\", and the URL of its connection to this grid operator."
                   ];

        }

        private static String ConnectionEntryBinding(VENConnectionMade Made)

            => protocols.WWCP.Node.Connections.ConnectionEntry.AsText(Made.Connection.OpenADRBinding) ?? "messages";

        private String[] Disconnected(String[] Arguments)
        {

            if (Arguments.Length != 2)
                return [ $"Usage: {CommandName} disconnect <venName> <connectionId>" ];

            return cli.GridOperator.TryDisconnectLocalController(Arguments[0], Arguments[1], cli.Who, out var error, out _)
                       ? [ $"The connection '{Arguments[1]}' of the local controller '{Arguments[0]}' is taken away, and the login it came under." ]
                       : [ error ];

        }

        private String[] Credentials(String Sentence, String ClientId, String Secret)

            => [
                   Sentence,
                   $"  token URL      {cli.GridOperator.OpenADRTokenURL}",
                   $"  client ID      {ClientId}",
                   $"  client secret  {Secret}"
               ];

        #endregion

    }

}
