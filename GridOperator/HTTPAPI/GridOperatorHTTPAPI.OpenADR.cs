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

    public partial class GridOperatorHTTPAPI
    {

        #region (private) GetOpenADR(Request)

        private Task<HTTPResponse> GetOpenADR(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.OpenADR), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.OpenADRJSON())
                   );

        }

        #endregion


        #region (private) GetLocalControllers(Request)

        private Task<HTTPResponse> GetLocalControllers(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.OpenADR), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, new JObject(
                           new JProperty("tokenURL",          GridOperator.OpenADRTokenURL),
                           new JProperty("vtnURL",            GridOperator.OpenADRVTNURL),
                           new JProperty("localControllers",  new JArray(GridOperator.OpenADR.Logins().Select(GridOperator.LocalControllerJSON)))
                       ))
                   );

        }

        #endregion

        #region (private) PostLocalController(Request) / PostLocalControllerSecret(Request) / DeleteLocalController(Request)

        /// <summary>
        /// POST /api/v1/openADR/localControllers with { "venName",
        /// "chargingPoolId", "gridConnectionPointId", "description" }: 201 with
        /// the local controller and its secret - the one time it is said.
        /// </summary>
        private Task<HTTPResponse> PostLocalController(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.OpenADR), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!GridOperator.TryAddLocalController(json, user.Id.ToString(), out var login, out var secret, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.Created, WithCredentials(GridOperator.LocalControllerJSON(login), login.VENName, secret))
                   );

        }

        /// <summary>
        /// POST /api/v1/openADR/localControllers/{venName}/secret: a new secret, said once.
        /// </summary>
        private Task<HTTPResponse> PostLocalControllerSecret(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.OpenADR), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!GridOperator.TryNewSecret(IdOf(Request), user.Id.ToString(), out var login, out var secret, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, WithCredentials(GridOperator.LocalControllerJSON(login), login.VENName, secret))
                   );

        }

        private Task<HTTPResponse> DeleteLocalController(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Edit(GridOperatorAccess.OpenADR), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!GridOperator.TryRemoveLocalController(IdOf(Request), user.Id.ToString(), out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return GetLocalControllers(Request);

        }

        #endregion


        #region (private) GetLimits(Request) / PostLimit(Request) / DeleteLimit(Request)

        private Task<HTTPResponse> GetLimits(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.OpenADR), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.LimitsJSON())
                   );

        }

        /// <summary>
        /// POST /api/v1/openADR/limits with { "kind", "limitKW", "start",
        /// "durationMinutes", "gridConnectionPointId", "name", "priority" }:
        /// 201 with the limit sent.
        /// </summary>
        private Task<HTTPResponse> PostLimit(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(GridOperatorAccess.OpenADR), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!TryParseJSONObject(Request, out var json, out var errorResponse))
                return Task.FromResult(errorResponse);

            if (!GridOperator.TrySendLimit(json, user.Id.ToString(), out var gridEvent, out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.Created, gridEvent.ToJSON(GridOperator.TimeProvider.GetUtcNow()))
                   );

        }

        private Task<HTTPResponse> DeleteLimit(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Run(GridOperatorAccess.OpenADR), true, out var user, out var refused))
                return Task.FromResult(refused);

            if (!GridOperator.TryCancelLimit(IdOf(Request), user.Id.ToString(), out var error, out var refusal))
                return Task.FromResult(Refused(Request, refusal, error));

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.LimitsJSON())
                   );

        }

        #endregion

        #region (private) GetReports(Request)

        private Task<HTTPResponse> GetReports(HTTPRequest Request)
        {

            if (!TryAuthorize(Request, Permission.Read(GridOperatorAccess.OpenADR), false, out _, out var refused))
                return Task.FromResult(refused);

            return Task.FromResult(
                       JSONResponse(Request, HTTPStatusCode.OK, GridOperator.ReportsJSON())
                   );

        }

        #endregion


        #region (private) WithCredentials(JSON, ClientId, Secret)

        /// <summary>
        /// A local controller, and what it signs in with: the one answer that says its secret.
        /// </summary>
        private JObject WithCredentials(JObject  JSON,
                                        String   ClientId,
                                        String   Secret)
        {

            JSON.Add(new JProperty("credentials", new JObject(
                         new JProperty("tokenURL",      GridOperator.OpenADRTokenURL),
                         new JProperty("vtnURL",        GridOperator.OpenADRVTNURL),
                         new JProperty("clientID",      ClientId),
                         new JProperty("clientSecret",  Secret)
                     )));

            return JSON;

        }

        #endregion

    }

}
