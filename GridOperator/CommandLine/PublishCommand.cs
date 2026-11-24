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

#endregion

namespace cloud.charging.open.GridOperator.CommandLine
{

    /// <summary>
    /// The grid operator and its grid connection points, published to the
    /// roaming network: what that would change, or - with "now" - one change
    /// set of its head, signed by the grid operator and committed; with
    /// "accept", the change set of the roaming network's operator that adds it,
    /// signed with its identity key.
    /// </summary>
    public class PublishCommand(GridOperatorCLI CLI) : ACLICommand<GridOperatorCLI>(CLI),
                                                      ICLICommand
    {

        #region Data

        /// <summary>
        /// The name this is typed as.
        /// </summary>
        public static readonly String CommandName = nameof(PublishCommand)[..^7].ToLowerFirstChar();

        #endregion

        #region Suggest(Arguments)

        public override IEnumerable<SuggestionResponse> Suggest(String[] Arguments)
        {

            if (Arguments.Length == 1)
                return CommandName.StartsWith(Arguments[0], StringComparison.OrdinalIgnoreCase)
                           ? [ SuggestionResponse.CommandCompleted(CommandName) ]
                           : [];

            if (Arguments.Length == 2 && CommandName.Equals(Arguments[0], StringComparison.OrdinalIgnoreCase))
                return new[] { "now", "accept" }.
                           Where (word => word.StartsWith(Arguments[1], StringComparison.OrdinalIgnoreCase)).
                           Select(word => SuggestionResponse.ParameterCompleted($"{CommandName} {word}")).
                           ToArray();

            return [];

        }

        #endregion

        #region Execute(Arguments, CancellationToken)

        public override async Task<String[]> Execute(String[]           Arguments,
                                                     CancellationToken  CancellationToken)
        {

            var now     = Arguments.Length == 2 && Arguments[1].Equals("now",    StringComparison.OrdinalIgnoreCase);
            var accept  = Arguments.Length == 2 && Arguments[1].Equals("accept", StringComparison.OrdinalIgnoreCase);

            if (Arguments.Length > 2 || (Arguments.Length == 2 && !now && !accept))
                return [ $"Usage: {Help()}" ];

            if (!cli.MayDo(now || accept ? Permission.Run (GridOperatorAccess.POI)
                                         : Permission.Read(GridOperatorAccess.POI),
                           now ? $"{CommandName} now" : accept ? $"{CommandName} accept" : CommandName,
                           out var refused))
                return [ refused ];

            var result = now    ? await cli.GridOperator.Publish           (cli.Who, CancellationToken)
                       : accept ? await cli.GridOperator.AcceptAddition    (cli.Who, CancellationToken)
                       :          await cli.GridOperator.PreviewPublication(CancellationToken);

            return [
                       result.Message,
                       .. (result.Plan?.Steps ?? []).Select(step => $"  {step.Kind,-9}  {step.Text}"),
                       .. (!now && !accept && result.Success && result.Plan is { IsParty: true, IsEmpty: false }
                              ? new[] { $"'{CommandName} now' signs it and commits it." }
                              : []),
                       .. (!now && !accept && result.Success && result.Awaiting is { Count: > 0 }
                              ? new[] { $"'{CommandName} accept' signs it with the identity key." }
                              : [])
                   ];

        }

        #endregion

        #region Help()

        public override String Help()

            => $"{CommandName} [now|accept] - what publishing the grid operator to its roaming network would change; with 'now', signed and committed; " +
                "with 'accept', the change set adding it there signed with its identity key";

        #endregion

    }

}
