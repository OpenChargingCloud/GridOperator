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

using System.Text;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.OpenADRv3;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// The virtual top node (VTN) of OpenADR 3 this grid operator is, as the
    /// local controllers of the charging pools on its grid - its VENs - talk to
    /// it: below "/openadr3", beside the JSON API of the web interface and with
    /// a sign-in of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A local controller signs in as OpenADR 3 says, with the client
    /// credentials grant of OAuth 2.0: POST auth/token with its client
    /// identification - its VEN name - and the secret it was given when it was
    /// added here, as a form, as HTTP Basic or as JSON. What it gets is a
    /// bearer token for an hour.
    /// </para>
    /// <para>
    /// With it, it reads; it changes nothing but its own reports:
    /// </para>
    /// <para>
    /// | | |
    /// |---|---|
    /// | <c>POST auth/token</c> | a bearer token, for the client credentials of a local controller |
    /// | <c>GET auth/server</c> | where the token is had |
    /// | <c>GET programs</c>, <c>programs/{programID}</c> | the programs of this grid operator |
    /// | <c>GET events</c>, <c>events/{eventID}</c> | the limits meant for this local controller - targeted at its VEN name - with <c>?programID</c>, <c>?skip</c> and <c>?limit</c> |
    /// | <c>GET vens</c>, <c>vens/{venID}</c> | itself, and nobody else |
    /// | <c>POST reports</c> | a report of what it did, on an event meant for it |
    /// | <c>GET reports</c>, <c>reports/{reportID}</c> | its own reports |
    /// </para>
    /// <para>
    /// Programs and events are made by the grid operator - through its web
    /// interface, its JSON API or its command line - and never by a VEN: a
    /// POST, PUT or DELETE of one here is a 403. Every refusal is a problem
    /// of RFC 9457, as OpenADR 3 answers with.
    /// </para>
    /// </remarks>
    public class OpenADRVTNAPI : HTTPAPI
    {

        #region Data

        /// <summary>
        /// Where the VTN is, below the base path of the grid operator.
        /// </summary>
        public const  String  PathSegment       = "openadr3";

        /// <summary>
        /// The most objects one answer lists, as OpenADR 3 defaults to.
        /// </summary>
        public const  Int32   MaxLimit          = 50;

        #endregion

        #region Properties

        /// <summary>
        /// The grid operator this VTN speaks for.
        /// </summary>
        public GridOperator  GridOperator  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Create and register the VTN within the given HTTP server.
        /// </summary>
        /// <param name="HTTPServer">The HTTP server.</param>
        /// <param name="GridOperator">The grid operator this VTN speaks for.</param>
        /// <param name="RootPath">Where it is: "/openadr3" below the base path of the grid operator.</param>
        public OpenADRVTNAPI(HTTPServer    HTTPServer,
                             GridOperator  GridOperator,
                             HTTPPath      RootPath)

            : base(HTTPServer,
                   RootPath: RootPath)

        {

            this.GridOperator = GridOperator;

            RegisterURLTemplates();

        }

        #endregion


        #region (private) RegisterURLTemplates()

        private void RegisterURLTemplates()
        {

            AddHandler(HTTPPath.Root + "auth/token",               PostToken,      HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "auth/server",              GetAuthServer,  HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "programs",                 GetPrograms,    HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "programs/{programID}",     GetProgram,     HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "events",                   GetEvents,      HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "events/{eventID}",         GetEvent,       HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "vens",                     GetVENs,        HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "vens/{venID}",             GetVEN,         HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "reports",                  GetReports,     HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "reports",                  PostReport,     HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "reports/{reportID}",       GetReport,      HTTPMethod.GET);

            // What only the grid operator does: a VEN that tries is told so,
            // rather than that there is no such thing.
            foreach (var path in new[] { "programs", "programs/{programID}", "events", "events/{eventID}", "vens", "vens/{venID}" })
                foreach (var method in new[] { HTTPMethod.POST, HTTPMethod.PUT, HTTPMethod.DELETE })
                    if (!(method == HTTPMethod.POST && path.Contains('{')))
                        AddHandler(HTTPPath.Root + path, Forbidden, method);

            // Everything else below /openadr3 is a problem of its own, and
            // never the single-page-application stub of the web interface.
            foreach (var method in new[] { HTTPMethod.GET, HTTPMethod.HEAD, HTTPMethod.POST, HTTPMethod.PUT, HTTPMethod.PATCH, HTTPMethod.DELETE })
                AddHandler(HTTPPath.Root + "{path..}", UnknownPath, method);

        }

        #endregion


        #region (private) PostToken(Request) / GetAuthServer(Request)

        /// <summary>
        /// POST auth/token: a bearer token for the client credentials of a
        /// local controller - as a form with grant_type=client_credentials,
        /// client_id and client_secret, as HTTP Basic, or as JSON with
        /// clientID and clientSecret.
        /// </summary>
        private Task<HTTPResponse> PostToken(HTTPRequest Request)
        {

            var (clientId, clientSecret, grantType) = CredentialsOf(Request);

            if (grantType is not null && grantType != "client_credentials")
                return Task.FromResult(OAuthError(Request, HTTPStatusCode.BadRequest, "unsupported_grant_type", "Only the grant type 'client_credentials' is taken."));

            if (String.IsNullOrEmpty(clientId) || String.IsNullOrEmpty(clientSecret))
                return Task.FromResult(OAuthError(Request, HTTPStatusCode.BadRequest, "invalid_request", "Say who you are: client_id and client_secret, as a form, as HTTP Basic or as JSON."));

            var issued = GridOperator.OpenADR.TryIssueToken(clientId, clientSecret);

            if (issued is not (String token, TimeSpan expiresIn))
            {
                GridOperator.Log.Warning($"A local controller signing in at the OpenADR VTN as '{clientId}' was refused: no such local controller, or not its secret.", "openadr", "auth");
                return Task.FromResult(OAuthError(Request, HTTPStatusCode.Unauthorized, "invalid_client", "No local controller signs in with these client credentials."));
            }

            GridOperator.Log.Info($"The local controller '{clientId}' signed in at the OpenADR VTN.", "openadr", "auth");

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JObject(
                           new JProperty("access_token",  token),
                           new JProperty("token_type",    "Bearer"),
                           new JProperty("expires_in",    (Int64) expiresIn.TotalSeconds),
                           new JProperty("scope",         "read_all write_reports")
                       ))
                   );

        }

        /// <summary>
        /// GET auth/server: where the token is had.
        /// </summary>
        private Task<HTTPResponse> GetAuthServer(HTTPRequest Request)

            => Task.FromResult(
                   JSON(Request, HTTPStatusCode.OK, new JObject(
                       new JProperty("tokenURL", GridOperator.OpenADRTokenURL)
                   ))
               );

        #endregion

        #region (private) GetPrograms(Request) / GetProgram(Request)

        private Task<HTTPResponse> GetPrograms(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(GridOperator.ProgramJSON()))
                   );

        }

        private Task<HTTPResponse> GetProgram(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            var id = IdOf(Request);

            return Task.FromResult(
                       id == GridOperator.Program.Id?.ToString()
                           ? JSON   (Request, HTTPStatusCode.OK, GridOperator.ProgramJSON())
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no program '{id}'.")
                   );

        }

        #endregion

        #region (private) GetEvents(Request) / GetEvent(Request)

        /// <summary>
        /// GET events: the limits meant for the local controller that asks,
        /// those still to come and those going on - "?programID", "?skip" and
        /// "?limit" as OpenADR 3 has them.
        /// </summary>
        private Task<HTTPResponse> GetEvents(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var programId  = Request.QueryString.GetString("programID");
            var now        = GridOperator.TimeProvider.GetUtcNow();

            var meant      = GridOperator.OpenADR.Events().
                                          Where  (gridEvent => gridEvent.IsFor(login.VENName)).
                                          Where  (gridEvent => programId is null || gridEvent.Event.ProgramId.ToString() == programId).
                                          Where  (gridEvent => gridEvent.End > now).
                                          OrderBy(gridEvent => gridEvent.Start).
                                          ToArray();

            if (!TryPage(Request, meant, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(gridEvent => gridEvent.EventJSON())))
                   );

        }

        private Task<HTTPResponse> GetEvent(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id         = IdOf(Request);
            var gridEvent  = GridOperator.OpenADR.Event(id);

            // One meant for another local controller is none of this one's
            // business, not even that it exists.
            return Task.FromResult(
                       gridEvent is not null && gridEvent.IsFor(login.VENName)
                           ? JSON   (Request, HTTPStatusCode.OK, gridEvent.EventJSON())
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no event '{id}' for '{login.VENName}'.")
                   );

        }

        #endregion

        #region (private) GetVENs(Request) / GetVEN(Request)

        private Task<HTTPResponse> GetVENs(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(VENJSON(login)))
                   );

        }

        private Task<HTTPResponse> GetVEN(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id = IdOf(Request);

            return Task.FromResult(
                       id == login.VENId.ToString()
                           ? JSON   (Request, HTTPStatusCode.OK, VENJSON(login))
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no VEN '{id}' for '{login.VENName}'.")
                   );

        }

        #endregion

        #region (private) GetReports(Request) / GetReport(Request) / PostReport(Request)

        private Task<HTTPResponse> GetReports(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var own = GridOperator.OpenADR.Reports().
                                   Where(report => report.ClientName.Equals(login.VENName, StringComparison.OrdinalIgnoreCase)).
                                   ToArray();

            if (!TryPage(Request, own, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(ReportJSON)))
                   );

        }

        private Task<HTTPResponse> GetReport(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id      = IdOf(Request);
            var report  = GridOperator.OpenADR.Reports().
                                       FirstOrDefault(report => report.Id?.ToString() == id &&
                                                                report.ClientName.Equals(login.VENName, StringComparison.OrdinalIgnoreCase));

            return Task.FromResult(
                       report is not null
                           ? JSON   (Request, HTTPStatusCode.OK, ReportJSON(report))
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no report '{id}' of '{login.VENName}'.")
                   );

        }

        /// <summary>
        /// POST reports: what a local controller did on an event meant for it.
        /// Its client name is its VEN name, whatever it says; 201 with the
        /// report as it is kept.
        /// </summary>
        private Task<HTTPResponse> PostReport(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            JObject json;

            try
            {
                json = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "");
            }
            catch (Exception e)
            {
                return Task.FromResult(Problem(Request, HTTPStatusCode.BadRequest, "Bad request", $"The report is no JSON object: {e.Message}"));
            }

            if (!Report.TryParse(json, out var report, out var error))
                return Task.FromResult(Problem(Request, HTTPStatusCode.BadRequest, "Bad request", error));

            var gridEvent = GridOperator.OpenADR.Event(report.EventId.ToString());

            if (gridEvent is null || !gridEvent.IsFor(login.VENName))
                return Task.FromResult(Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no event '{report.EventId}' for '{login.VENName}' to report on."));

            if (gridEvent.Event.ProgramId.ToString() != report.ProgramId.ToString())
                return Task.FromResult(Problem(Request, HTTPStatusCode.BadRequest, "Bad request", $"The event '{report.EventId}' is of the program '{gridEvent.Event.ProgramId}', not of '{report.ProgramId}'."));

            var now   = GridOperator.TimeProvider.GetUtcNow();
            var kept  = new Report(report.ProgramId,
                                   report.EventId,
                                   login.VENName,
                                   report.Resources,
                                   report.ReportName,
                                   report.PayloadDescriptors,
                                   Report_Id.NewRandom,
                                   now,
                                   now);

            GridOperator.OpenADR.AddReport(kept);

            GridOperator.Log.Info($"The local controller '{login.VENName}' reported on the event '{report.EventId}'" +
                                  (report.ReportName is not null ? $": {report.ReportName}." : "."),
                                  "openadr", "report");

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.Created, ReportJSON(kept))
                   );

        }

        #endregion

        #region (private) Forbidden(Request) / UnknownPath(Request)

        private Task<HTTPResponse> Forbidden(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       Problem(Request, HTTPStatusCode.Forbidden, "Forbidden",
                               "Programs, events and VENs are made by the grid operator; a VEN reads them, and writes its reports.")
                   );

        }

        private Task<HTTPResponse> UnknownPath(HTTPRequest Request)

            => Task.FromResult(
                   Problem(Request, HTTPStatusCode.NotFound, "Not found", $"The VTN has nothing at '{Request.Path}'.")
               );

        #endregion


        #region (private) SignedIn(Request, out Refused)

        /// <summary>
        /// The local controller behind the bearer token of the request - or
        /// null, and the 401 that says why.
        /// </summary>
        private LocalControllerLogin? SignedIn(HTTPRequest        Request,
                                               out HTTPResponse?  Refused)
        {

            var authorization = Request.GetHeaderField("Authorization") ?? "";

            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                GridOperator.OpenADR.LoginOfToken(authorization[7..].Trim()) is LocalControllerLogin login)
            {
                Refused = null;
                return login;
            }

            Refused = Problem(Request, HTTPStatusCode.Unauthorized, "Unauthorized",
                              $"Sign in first: a bearer token from {GridOperator.OpenADRTokenURL}, for the client credentials of a local controller.");

            return null;

        }

        #endregion

        #region (private static) CredentialsOf(Request)

        /// <summary>
        /// The client credentials of a token request: HTTP Basic, a form or JSON.
        /// </summary>
        private static (String? ClientId, String? ClientSecret, String? GrantType) CredentialsOf(HTTPRequest Request)
        {

            String? clientId      = null;
            String? clientSecret  = null;
            String? grantType     = null;

            var authorization     = Request.GetHeaderField("Authorization") ?? "";

            if (authorization.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pair  = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[6..].Trim()));
                    var colon = pair.IndexOf(':');
                    if (colon > 0)
                    {
                        clientId      = Uri.UnescapeDataString(pair[..colon]);
                        clientSecret  = Uri.UnescapeDataString(pair[(colon + 1)..]);
                    }
                }
                catch (FormatException)
                {
                    // Taken for no credentials at all.
                }
            }

            var body = (Request.HTTPBodyAsUTF8String ?? "").Trim();

            if (body.StartsWith('{'))
            {
                try
                {
                    var json      = JObject.Parse(body);
                    clientId    ??= json.Value<String>("clientID")     ?? json.Value<String>("client_id");
                    clientSecret ??= json.Value<String>("clientSecret") ?? json.Value<String>("client_secret");
                    grantType     = json.Value<String>("grant_type");
                }
                catch (JsonException)
                {
                    // Taken for no credentials at all.
                }
            }

            else if (body.Length > 0)
            {
                foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {

                    var equals  = part.IndexOf('=');
                    var name    = Uri.UnescapeDataString((equals < 0 ? part : part[..equals]).Replace('+', ' '));
                    var value   = equals < 0 ? "" : Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' '));

                    switch (name)
                    {
                        case "client_id":      clientId     ??= value;  break;
                        case "client_secret":  clientSecret ??= value;  break;
                        case "grant_type":     grantType      = value;  break;
                    }

                }
            }

            return (clientId?.Trim(), clientSecret?.Trim(), grantType?.Trim());

        }

        #endregion

        #region (private static) TryPage(Request, All, out Page, out BadPage)

        /// <summary>
        /// The part of a list "?skip" and "?limit" ask for, as OpenADR 3 has them:
        /// none skipped and at most 50 by default.
        /// </summary>
        private static Boolean TryPage<T>(HTTPRequest       Request,
                                          IReadOnlyList<T>  All,
                                          out T[]           Page,
                                          out HTTPResponse  BadPage)
        {

            var skipText   = Request.QueryString.GetString("skip");
            var limitText  = Request.QueryString.GetString("limit");
            var skip       = 0;
            var limit      = MaxLimit;

            if ((skipText  is not null && (!Int32.TryParse(skipText,  out skip)  || skip  < 0)) ||
                (limitText is not null && (!Int32.TryParse(limitText, out limit) || limit < 0 || limit > MaxLimit)))
            {
                Page     = [];
                BadPage  = Problem(Request, HTTPStatusCode.BadRequest, "Bad request", $"'skip' is a number of at least 0, 'limit' one from 0 to {MaxLimit}.");
                return false;
            }

            Page     = [ .. All.Skip(skip).Take(limit) ];
            BadPage  = null!;
            return true;

        }

        #endregion

        #region (private static) VENJSON(Login) / ReportJSON(Report) / IdOf(Request)

        private static JObject VENJSON(LocalControllerLogin Login)
        {
            var json = Login.ToVirtualEndNode().ToJSON();
            json["objectType"] = "VEN";
            return json;
        }

        private static JObject ReportJSON(Report Report)
        {
            var json = Report.ToJSON();
            json["objectType"] = "REPORT";
            return json;
        }

        private static String IdOf(HTTPRequest Request)

            => Request.ParsedURLParameters.Length > 0
                   ? Request.ParsedURLParameters[0].Trim()
                   : "";

        #endregion

        #region (private static) JSON(...) / Problem(...) / OAuthError(...)

        /// <summary>
        /// An answer in JSON, never cached.
        /// </summary>
        private static HTTPResponse JSON(HTTPRequest     Request,
                                         HTTPStatusCode  StatusCode,
                                         JToken          JSON)

            => new HTTPResponse.Builder(Request) {
                   HTTPStatusCode  = StatusCode,
                   ContentType     = HTTPContentType.Application.JSON_UTF8,
                   Content         = Encoding.UTF8.GetBytes(JSON.ToString(Formatting.None)),
                   CacheControl    = "no-store"
               }.AsImmutable;

        /// <summary>
        /// A problem, as RFC 9457 and OpenADR 3 say one.
        /// </summary>
        private static HTTPResponse Problem(HTTPRequest     Request,
                                            HTTPStatusCode  StatusCode,
                                            String          Title,
                                            String          Detail)

            => JSON(Request, StatusCode, new JObject(
                   new JProperty("type",    "about:blank"),
                   new JProperty("title",   Title),
                   new JProperty("status",  StatusCode.Code),
                   new JProperty("detail",  Detail)
               ));

        /// <summary>
        /// An error of a token request, as OAuth 2.0 says one.
        /// </summary>
        private static HTTPResponse OAuthError(HTTPRequest     Request,
                                               HTTPStatusCode  StatusCode,
                                               String          Error,
                                               String          Description)

            => JSON(Request, StatusCode, new JObject(
                   new JProperty("error",              Error),
                   new JProperty("error_description",  Description)
               ));

        #endregion

    }

}
