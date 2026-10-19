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

using cloud.charging.open.protocols.WWCP.Node.Web;

#endregion

namespace cloud.charging.open.GridOperator
{

    public partial class GridOperatorHTTPAPI
    {

        #region (private) GetGridOperator(Request) / PutGridOperator(Request)

        /// <summary>
        /// GET /api/v1/gridOperator: the grid operator, its keys and its party.
        /// </summary>
        private Task<HTTPResponse> GetGridOperator(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.POI), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.GridOperatorJSON())
                   );

        }

        /// <summary>
        /// PUT /api/v1/gridOperator with any of { "name", "description",
        /// "address", "homepage", "hotline", "dataSource", "customData" }, as
        /// POI 3 writes them - null or empty removes one. Answers with what it
        /// says now.
        /// </summary>
        private Task<HTTPResponse> PutGridOperator(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!GridOperator.TryUpdateGridOperator(json, user.Id.ToString(), out var error, out var notSaved))
                return Task.FromResult(NotChanged(Request, HTTPStatusCode.BadRequest, error, notSaved));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.GridOperatorJSON())
                   );

        }

        #endregion


        #region (private) GetGridConnectionPoints(Request) / GetGridConnectionPoint(Request)

        private Task<HTTPResponse> GetGridConnectionPoints(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.POI), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.GridConnectionPointsJSON())
                   );

        }

        private Task<HTTPResponse> GetGridConnectionPoint(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.POI), false, out _, out var refused))
                return Task.FromResult(refused);

            var id = IdOf(Request);

            if (!GridOperator.TryGetGridConnectionPoint(id, out var point))
                return Task.FromResult(ErrorJSON(Request, HTTPStatusCode.NotFound, $"The grid operator '{GridOperator.Own.Id}' has no grid connection point '{id}'."));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.GridConnectionPointJSON(point))
                   );

        }

        #endregion

        #region (private) PostGridConnectionPoint(Request) / PutGridConnectionPoint(Request) / DeleteGridConnectionPoint(Request)

        /// <summary>
        /// POST /api/v1/gridConnectionPoints with a grid connection point as
        /// POI 3 writes one: 201 and the point; 409 where there is one of that
        /// identification already.
        /// </summary>
        private Task<HTTPResponse> PostGridConnectionPoint(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!GridOperator.TryAddGridConnectionPoint(json, user.Id.ToString(), out var point, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.Created, GridOperator.GridConnectionPointJSON(point))
                   );

        }

        /// <summary>
        /// PUT /api/v1/gridConnectionPoints/{id}: the point, said anew whole.
        /// </summary>
        private Task<HTTPResponse> PutGridConnectionPoint(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!GridOperator.TryReplaceGridConnectionPoint(IdOf(Request), json, user.Id.ToString(), out var point, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.GridConnectionPointJSON(point))
                   );

        }

        /// <summary>
        /// DELETE /api/v1/gridConnectionPoints/{id}: 409 while a local controller is behind it.
        /// </summary>
        private Task<HTTPResponse> DeleteGridConnectionPoint(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!GridOperator.TryRemoveGridConnectionPoint(IdOf(Request), user.Id.ToString(), out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.GridConnectionPointsJSON())
                   );

        }

        #endregion


        #region (private) GetRoamingNetworkSettings(Request) / PutRoamingNetworkSettings(Request)

        private Task<HTTPResponse> GetRoamingNetworkSettings(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.POI), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.RoamingNetworkSettingsJSON())
                   );

        }

        /// <summary>
        /// PUT /api/v1/configuration/roamingNetwork with { "url" }: another
        /// roaming network to publish to - or none, with an empty one.
        /// </summary>
        private Task<HTTPResponse> PutRoamingNetworkSettings(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.POI), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!GridOperator.TryUpdateRoamingNetworkSettings(json, user.Id.ToString(), out var error, out var notSaved))
                return Task.FromResult(NotChanged(Request, HTTPStatusCode.BadRequest, error, notSaved));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.RoamingNetworkSettingsJSON())
                   );

        }

        #endregion

        #region (private) GetPublication(Request) / PostPublication(Request)

        /// <summary>
        /// GET /api/v1/roamingNetwork/publication: what publishing would change
        /// in the roaming network now - asked of it, nothing sent.
        /// </summary>
        private async Task<HTTPResponse> GetPublication(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.POI), false, out _, out var refused))
                return refused;

            var result = await GridOperator.PreviewPublication();

            return result.Success
                       ? JSONResponse(Request, HTTPStatusCode.OK, result.ToJSON(GridOperator.RoamingNetworkSettings.URL))
                       : Refused     (Request, result.Refusal ?? ChangeRefusal.Upstream, result.Message);

        }

        /// <summary>
        /// POST /api/v1/roamingNetwork/publication: one change set of the head
        /// of the roaming network, signed by the grid operator and committed.
        /// 409 where the roaming network has not added it yet, 502 where it
        /// refused or could not be reached.
        /// </summary>
        private async Task<HTTPResponse> PostPublication(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(GridOperatorAccess.POI), true, out var user, out var refused))
                return refused;

            var result = await GridOperator.Publish(user.Id.ToString());

            return result.Success
                       ? JSONResponse(Request, HTTPStatusCode.OK, result.ToJSON(GridOperator.RoamingNetworkSettings.URL))
                       : ErrorJSON   (Request,
                                      result.Refusal switch {
                                          ChangeRefusal.Conflict  => HTTPStatusCode.Conflict,
                                          ChangeRefusal.Invalid   => HTTPStatusCode.BadRequest,
                                          _                       => HTTPStatusCode.BadGateway
                                      },
                                      result.Message,
                                      new Newtonsoft.Json.Linq.JProperty("publication", result.ToJSON(GridOperator.RoamingNetworkSettings.URL)));

        }

        #endregion

    }

}
