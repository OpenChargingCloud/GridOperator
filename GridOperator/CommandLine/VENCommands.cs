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

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.Node.Web;
using cloud.charging.open.protocols.WWCP.Node.Logins;
using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Connections;

using GO = cloud.charging.open.GridOperator.GridOperator;

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    #region VensCommand

    /// <summary>
    /// The local controllers that connect over a WebSocket or SSH - the server
    /// they connect to, and each with its connection, its binding, its VEN,
    /// what it signs in with and whether it is connected now. The ones that
    /// sign in over HTTPS with a client secret are 'localControllers'.
    /// </summary>
    public class VensCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                    ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public const String CommandName = "vens";

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)

            => Arguments.Length == 1 && CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                   ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                   : [];

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override Task<String[]> Execute(String[]           Arguments,
                                               CancellationToken  CancellationToken)
        {

            if (!cli.MayDo(Permission.Read(GridOperatorAccess.OpenADR), CommandName, out var refused))
                return Task.FromResult<String[]>([ refused ]);

            var grid       = cli.GridOperator;
            var settings   = grid.VENServerSettings;
            var connected  = grid.VENLoginsIn.ToHashSet(StringComparer.Ordinal);
            var lines      = new List<String>();

            #region The server

            lines.Add(settings.IsEnabled
                          ? grid.VENServerRunning
                                ? $"The WebSocket server of OpenADR listens at port {settings.TCPPort?.ToUInt16()}{(grid.VENServerTLS ? ", with TLS" : ", without TLS")}."
                                : $"The WebSocket server of OpenADR is switched on at port {settings.TCPPort?.ToUInt16()}, and does not listen."
                          : "The WebSocket server of OpenADR is switched off.");

            lines.Add(grid.OpenADROverSSHServed
                          ? $"OpenADR is served over SSH, at port {grid.SSHPort?.ToUInt16()}."
                          : "OpenADR is not served over SSH.");

            #endregion

            #region Every local controller

            var entries = grid.Connections.Connections.
                              Where  (entry => entry.Inbound && entry.ConnectionType == ConnectionType.VEN).
                              OrderBy(entry => entry.InboundName, StringComparer.Ordinal).
                              ToArray();

            var named   = entries.Select(entry => entry.InboundName).OfType<String>().ToHashSet(StringComparer.Ordinal);

            if (entries.Length == 0)
                lines.Add("No local controller is written down to connect: none may.");

            foreach (var entry in entries)
            {

                var name = entry.InboundName ?? "?";

                lines.Add($"{name}  {(entry.AutoConnect ? "on" : "off")}  {(connected.Contains(name) ? "connected" : "not connected")}  " +
                          $"the VEN '{entry.VEN}', {BindingOf(entry)}  {entry.URL}  {entry.Description.FirstText()}");

                if (User_Id.TryParse(name) is User_Id login && grid.WebSocketLogins.TryGet(login, out var signsIn))
                    lines.Add($"  signs in with {String.Join(", ", WaysIn(signsIn))}, in the group '{signsIn.GroupId}'{(signsIn.Enabled ? "" : ", switched off")}");
                else
                    lines.Add("  has no login: it cannot sign in.");

            }

            // A login without a connection signs in and is let go of.
            foreach (var login in grid.WebSocketLogins.Logins.Where(login => !named.Contains(login.Login.ToString())).OrderBy(login => login.Login))
                lines.Add($"{login.Login}  has a login and no connection: it is let go of when it connects.");

            #endregion

            return Task.FromResult<String[]>([ .. lines ]);

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} - the local controllers that connect over a WebSocket or SSH, what they sign in with, and whether they are connected";

        #endregion


        #region (private static) BindingOf(Entry)

        private static String BindingOf(ConnectionEntry Entry)

            => Entry.OpenADRBinding == OpenADRBinding.Messages
                   ? "in messages"
                   : "the REST API in a tunnel";

        #endregion

        #region (private static) WaysIn(Login)

        private static IEnumerable<String> WaysIn(WebSocketLogin Login)
        {

            var ways = new List<String>();

            if (Login.HasPassword)
                ways.Add("a password");

            if (Login.HasTOTP)
                ways.Add("TOTP");

            if (Login.HasSSHKey)
                ways.Add($"{Login.Keys.Count} SSH key(s)");

            if (Login.HasKeyPin)
                ways.Add($"{Login.Pins.Count} key pin(s)");

            return ways.Count > 0 ? ways : [ "nothing yet" ];

        }

        #endregion

    }

    #endregion

    #region VenKeysCommand

    /// <summary>
    /// The SSH keys a local controller signs in with, for OpenADR over SSH.
    /// </summary>
    public class VenKeysCommand(GridOperatorCLI CLI) : LoginKeysCommand(CLI, CommandName, GridOperatorAccess.OpenADR)
    {

        public const String CommandName = "venKeys";

        private GO Grid
            => ((GridOperatorCLI) cli).GridOperator;

        protected override WebSocketLogins        Logins      => Grid.WebSocketLogins;
        protected override String                 LoginNoun   => "local controller";
        protected override String                 LoginWord   => "login";
        protected override String                 KeysAreFor  => "for OpenADR over SSH";
        protected override IReadOnlyList<String>  LogTags     => [ "openadr", "ven" ];

        protected override IEnumerable<String> WhyNoLoginSignsIn()
        {
            if (!Grid.OpenADROverSSHServed)
                yield return "OpenADR is not served over SSH: no local controller signs in over SSH.";
        }

    }

    #endregion

    #region VenPinsCommand

    /// <summary>
    /// The key pins a local controller signs in with, on the WebSocket port of OpenADR.
    /// </summary>
    public class VenPinsCommand(GridOperatorCLI CLI) : LoginPinsCommand(CLI, CommandName, GridOperatorAccess.OpenADR)
    {

        public const String CommandName = "venPins";

        private GO Grid
            => ((GridOperatorCLI) cli).GridOperator;

        protected override WebSocketLogins        Logins      => Grid.WebSocketLogins;
        protected override String                 LoginNoun   => "local controller";
        protected override String                 LoginWord   => "login";
        protected override String                 PinsAreFor  => "on the WebSocket port of OpenADR";
        protected override IReadOnlyList<String>  LogTags     => [ "openadr", "ven" ];

        protected override IEnumerable<String> WhyNoLoginSignsIn()
        {

            if (!Grid.VENServerSettings.IsEnabled)
                yield return "The WebSocket server of OpenADR is switched off: no local controller signs in.";

            else if (Grid.VENServerRunning && !Grid.VENServerTLS)
                yield return "The WebSocket port of OpenADR is not encrypted: no key pin lets a local controller in until it is.";

        }

    }

    #endregion

}
