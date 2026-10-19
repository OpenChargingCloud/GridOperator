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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// What a grid operator adds to the resources every node has, and the
    /// roles of the people who work with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two resources, because they are two different things to be trusted
    /// with. The points of interest are what the grid operator says of itself
    /// to everybody: who it is and where charging pools are connected to its
    /// grid - edited, they are what it vouches for; run, they are published to
    /// a roaming network, signed with its key. OpenADR is what it tells the
    /// local controllers of those pools to do: edited, which local controllers
    /// may sign in at all; run, a limit sent to them.
    /// </para>
    /// <para>
    /// The node brings the viewer, who may look at everything, and the
    /// administrators, who may do everything - "root" among them. The
    /// configuration file may add roles and say differently what one of them
    /// may do - see the node's "roles" section.
    /// </para>
    /// </remarks>
    public static class GridOperatorAccess
    {

        #region Resources

        /// <summary>
        /// The points of interest: read, the grid operator and its grid
        /// connection points; edited, either of them; run, published to the
        /// roaming network.
        /// </summary>
        public const String  POI      = "poi";

        /// <summary>
        /// OpenADR: read, the local controllers, the programs, the events and
        /// the reports; edited, which local controllers may sign in; run, an
        /// event sent or cancelled.
        /// </summary>
        public const String  OpenADR  = "openadr";

        /// <summary>
        /// Both.
        /// </summary>
        public static readonly IReadOnlyList<String>  Resources = [ POI, OpenADR ];

        #endregion

        #region Roles

        /// <summary>
        /// Somebody who keeps the grid connection points: edits them and the
        /// grid operator, publishes them to the roaming network, and looks at
        /// what OpenADR does - but sends nothing to a local controller.
        /// </summary>
        public static readonly Role  Planner     = new ("planner",
                                                        [ Permission.Read(POI),
                                                          Permission.Edit(POI),
                                                          Permission.Run (POI),
                                                          Permission.Read(OpenADR) ],
                                                        "keeps the grid connection points and publishes them, and looks at what OpenADR does");

        /// <summary>
        /// Somebody in the control room: looks at the grid connection points,
        /// and sends a limit to the local controllers behind one of them, or
        /// cancels it - but changes neither the points nor who may sign in.
        /// </summary>
        public static readonly Role  Dispatcher  = new ("dispatcher",
                                                        [ Permission.Read(POI),
                                                          Permission.Read(OpenADR),
                                                          Permission.Run (OpenADR) ],
                                                        "sends a limit to the local controllers behind a grid connection point, and cancels one");

        /// <summary>
        /// The two roles a grid operator adds.
        /// </summary>
        public static readonly IReadOnlyList<Role>  Roles = [ Planner, Dispatcher ];

        #endregion

    }

}
