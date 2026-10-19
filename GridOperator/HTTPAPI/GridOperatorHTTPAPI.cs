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

using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.Node.Logging;
using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator
{

    /// <summary>
    /// The JSON API the browser talks to, registered at "/api": what every
    /// node has - see <see cref="NodeHTTPAPI"/> - and the grid operator's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// | | |
    /// |---|---|
    /// | <c>GET v1/gridOperator</c>, <c>PUT</c> | the grid operator as POI 3 writes it, its keys and its party; PUT changes its name, description, address, homepage or hotline |
    /// | <c>GET v1/gridConnectionPoints</c>, <c>POST</c> | its grid connection points, with the local controllers behind each; POST adds one |
    /// | <c>GET v1/gridConnectionPoints/{id}</c>, <c>PUT</c>, <c>DELETE</c> | one of them |
    /// | <c>GET v1/configuration/roamingNetwork</c>, <c>PUT</c> | the roaming network it publishes to: <c>{ "url" }</c> |
    /// | <c>GET v1/roamingNetwork/publication</c> | what publishing would change there now |
    /// | <c>POST v1/roamingNetwork/publication</c> | publish: one change set, signed by the grid operator |
    /// | <c>GET v1/openADR</c> | the VTN: where it is, its program, how much it has |
    /// | <c>GET v1/openADR/localControllers</c>, <c>POST</c> | the local controllers that may sign in; POST adds one, 201 with its secret, once |
    /// | <c>DELETE v1/openADR/localControllers/{venName}</c> | one signs in no more |
    /// | <c>POST v1/openADR/localControllers/{venName}/secret</c> | a new secret for one, once |
    /// | <c>GET v1/openADR/limits</c>, <c>POST</c> | the limits sent; POST sends one |
    /// | <c>DELETE v1/openADR/limits/{id}</c> | cancel one |
    /// | <c>GET v1/openADR/reports</c> | what the local controllers reported |
    /// </para>
    /// </remarks>
    public partial class GridOperatorHTTPAPI : NodeHTTPAPI
    {

        #region Properties

        /// <summary>
        /// The grid operator this API speaks for.
        /// </summary>
        public GridOperator  GridOperator  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create and register the JSON API within the given HTTP server.
        /// </summary>
        /// <param name="HTTPServer">The HTTP server.</param>
        /// <param name="GridOperator">The grid operator this API speaks for.</param>
        /// <param name="ExtAPI">The accounts and the groups they are in.</param>
        /// <param name="Log">Everything that happens inside this grid operator.</param>
        /// <param name="APIPath">The root path of the API, the grid operator's own by default.</param>
        /// <param name="Version">The version reported by the status resource.</param>
        public GridOperatorHTTPAPI(HTTPServer    HTTPServer,
                                   GridOperator  GridOperator,
                                   HTTPExtAPI    ExtAPI,
                                   EventLog      Log,
                                   HTTPPath?     APIPath   = null,
                                   String?       Version   = null)

            : base(HTTPServer,
                   GridOperator,
                   ExtAPI,
                   Log,
                   APIPath,
                   Version ?? typeof(GridOperatorHTTPAPI).Assembly.GetName().Version?.ToString(3) ?? "0.0.0")

        {

            this.GridOperator = GridOperator;

            RegisterURLTemplates();

        }

        #endregion


        #region (private) RegisterURLTemplates()

        /// <summary>
        /// What only a grid operator has.
        /// </summary>
        private void RegisterURLTemplates()
        {

            AddHandler(HTTPPath.Root + "v1/gridOperator",                                    GetGridOperator,               HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/gridOperator",                                    PutGridOperator,               HTTPMethod.PUT);

            AddHandler(HTTPPath.Root + "v1/gridConnectionPoints",                            GetGridConnectionPoints,       HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/gridConnectionPoints",                            PostGridConnectionPoint,       HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/gridConnectionPoints/{id}",                       GetGridConnectionPoint,        HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/gridConnectionPoints/{id}",                       PutGridConnectionPoint,        HTTPMethod.PUT);
            AddHandler(HTTPPath.Root + "v1/gridConnectionPoints/{id}",                       DeleteGridConnectionPoint,     HTTPMethod.DELETE);

            AddHandler(HTTPPath.Root + "v1/configuration/roamingNetwork",                    GetRoamingNetworkSettings,     HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/configuration/roamingNetwork",                    PutRoamingNetworkSettings,     HTTPMethod.PUT);
            AddHandler(HTTPPath.Root + "v1/roamingNetwork/publication",                      GetPublication,                HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/roamingNetwork/publication",                      PostPublication,               HTTPMethod.POST);

            AddHandler(HTTPPath.Root + "v1/openADR",                                         GetOpenADR,                    HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/openADR/localControllers",                        GetLocalControllers,           HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/openADR/localControllers",                        PostLocalController,           HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/openADR/localControllers/{venName}",              DeleteLocalController,         HTTPMethod.DELETE);
            AddHandler(HTTPPath.Root + "v1/openADR/localControllers/{venName}/secret",       PostLocalControllerSecret,     HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/openADR/limits",                                  GetLimits,                     HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "v1/openADR/limits",                                  PostLimit,                     HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "v1/openADR/limits/{id}",                             DeleteLimit,                   HTTPMethod.DELETE);
            AddHandler(HTTPPath.Root + "v1/openADR/reports",                                 GetReports,                    HTTPMethod.GET);

        }

        #endregion


        #region (private static) IdOf(Request) / Refused(Request, Refusal, Error)

        /// <summary>
        /// The id in the path, unescaped: a grid connection point may be named "GCP 0815".
        /// </summary>
        private static String IdOf(HTTPRequest Request)

            => Request.ParsedURLParameters.Length > 0
                   ? Uri.UnescapeDataString(Request.ParsedURLParameters[0]).Trim()
                   : "";

        /// <summary>
        /// A no, with the status code its kind has.
        /// </summary>
        private static HTTPResponse Refused(HTTPRequest    Request,
                                            ChangeRefusal  Refusal,
                                            String         Error)

            => ErrorJSON(Request,
                         Refusal switch {
                             ChangeRefusal.NotFound  => HTTPStatusCode.NotFound,
                             ChangeRefusal.Conflict  => HTTPStatusCode.Conflict,
                             ChangeRefusal.NotSaved  => HTTPStatusCode.InternalServerError,
                             ChangeRefusal.Upstream  => HTTPStatusCode.BadGateway,
                             _                       => HTTPStatusCode.BadRequest
                         },
                         Error);

        #endregion

    }

}
