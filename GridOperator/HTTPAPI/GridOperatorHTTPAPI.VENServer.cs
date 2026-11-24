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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// The part of the JSON API about the local controllers that connect over
    /// a WebSocket or SSH - their VENs, speaking OpenADR over the connection:
    /// their connections, their logins and what they sign in with, the chains
    /// a client certificate may lead to, and the server they connect to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The routes of the connections and of the logins are every node's - see
    /// <see cref="NodeHTTPAPI.RegisterConnectionRoutes"/> and
    /// <see cref="NodeHTTPAPI.RegisterWebSocketLoginRoutes"/> in WWCP_Node -,
    /// so the grid operator's pages and a CSMS's are the same pages. All of
    /// them at the resource <see cref="GridOperatorAccess.OpenADR"/>, whose
    /// editing is saying which local controllers may sign in - over HTTPS with
    /// a client secret, and here over a connection of their own.
    /// </para>
    /// <para>
    /// A local controller is written down twice, as everybody who connects to
    /// a node is: as an inbound connection of the type "VEN" - which says that
    /// it may connect, with which binding and as which VEN, and which is
    /// switched off to shut it out - and as a login, which says what it signs
    /// in with.
    /// </para>
    /// </remarks>
    public partial class GridOperatorHTTPAPI
    {

        #region Data

        /// <summary>
        /// Where the routes of the WebSocket server of OpenADR and its logins are.
        /// </summary>
        public static readonly HTTPPath VENServerPath = HTTPPath.Root + "v1/configuration/ven-server";

        #endregion


        #region (private) RegisterVENServerURLTemplates()

        private void RegisterVENServerURLTemplates()
        {

            RegisterConnectionRoutes(GridOperatorAccess.OpenADR);

            AddHandler(VENServerPath, GetVENServer, HTTPMethod.GET);

            RegisterWebSocketLoginRoutes(
                new WebSocketLoginRoutes(
                    VENServerPath,
                    () => GridOperator.WebSocketLogins,
                    () => GridOperator.ClientTrust,
                    GridOperatorAccess.OpenADR,
                    "local controller",
                    [ "openadr", "ven" ]
                )
            );

        }

        #endregion

        #region (private) GetVENServer(Request)

        /// <summary>
        /// GET /api/v1/configuration/ven-server: the server the local
        /// controllers connect to - as the file says it, and as it is -, and
        /// who is connected now.
        /// </summary>
        /// <remarks>
        /// The server is changed in the "webSocketServer" section of the file,
        /// and the grid operator started again.
        /// </remarks>
        private Task<HTTPResponse> GetVENServer(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.OpenADR), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, new JObject(
                           new JProperty("configuration",  GridOperator.VENServerSettings.ToJSON()),
                           new JProperty("state",          new JObject(
                               new JProperty("running",    GridOperator.VENServerRunning),
                               new JProperty("tls",        GridOperator.VENServerTLS),
                               new JProperty("ssh",        GridOperator.OpenADROverSSHServed),
                               new JProperty("sshPort",    GridOperator.SSHPort?.ToUInt16())
                           )),
                           new JProperty("connected",      new JArray(GridOperator.VENLoginsIn.Order(StringComparer.Ordinal)))
                       ))
                   );

        }

        #endregion

    }

}
