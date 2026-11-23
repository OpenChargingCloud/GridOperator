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

using OpenADROperation = cloud.charging.open.protocols.OpenADRv3.Operation;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// The virtual top node (VTN) of OpenADR 3.1 this grid operator is, as the
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
    /// bearer token for an hour. Its VEN name is its clientID, too: what the
    /// VTN writes into its reports and subscriptions, and gives it back by.
    /// </para>
    /// <para>
    /// With it, it reads; it changes nothing but its own reports and
    /// subscriptions:
    /// </para>
    /// <para>
    /// | | |
    /// |---|---|
    /// | <c>POST auth/token</c> | a bearer token, for the client credentials of a local controller |
    /// | <c>GET auth/server</c> | where the token is had |
    /// | <c>GET programs</c>, <c>programs/{programID}</c> | the programs of this grid operator |
    /// | <c>GET events</c>, <c>events/{eventID}</c> | the limits this local controller may read - whose targets meet those granted to it - with <c>?programID</c>, <c>?targets</c>, <c>?active</c>, <c>?skip</c> and <c>?limit</c> |
    /// | <c>GET vens</c>, <c>vens/{venID}</c> | itself, and nobody else, with the targets granted to it |
    /// | <c>GET resources</c>, <c>resources/{resourceID}</c> | none: this grid operator keeps no resources of its VENs |
    /// | <c>POST reports</c>, <c>PUT</c> and <c>DELETE reports/{reportID}</c> | a report of what it did, on an event it may read |
    /// | <c>GET reports</c>, <c>reports/{reportID}</c> | its own reports |
    /// | <c>GET</c> and <c>POST subscriptions</c>, <c>GET</c>, <c>PUT</c> and <c>DELETE subscriptions/{subscriptionID}</c> | its own subscriptions, told of by webhooks |
    /// | <c>GET notifiers</c> | WEBHOOK, and no MQTT |
    /// </para>
    /// <para>
    /// Object privacy, as OpenADR 3.1 has it: a VEN reads a program or event
    /// with targets only where they meet those granted to it - met with those
    /// it asks for by <c>?targets</c>, if it asks - and sees of its targets
    /// only those. It reads, changes and removes only the reports and
    /// subscriptions of its own clientID; asked for another's, the answer is
    /// that there is none.
    /// </para>
    /// <para>
    /// Programs, events, VENs and resources are made by the grid operator -
    /// through its web interface, its JSON API or its command line - and never
    /// by a VEN: a POST, PUT or DELETE of one here is a 403. The MQTT topics
    /// below <c>notifiers/mqtt</c> are not there: this VTN has no broker. Every
    /// refusal is a problem of RFC 9457, as OpenADR 3 answers with - since
    /// 3.1 at <c>auth/token</c>, too.
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

        /// <summary>
        /// The scopes a token of a local controller is good for, OpenADR 3.1:
        /// reading programs and events by their targets, reading its own
        /// objects, writing its reports and its subscriptions.
        /// </summary>
        public const  String  VENScopes         = "read_targets read_ven_objects write_reports write_subscriptions";

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

            AddHandler(HTTPPath.Root + "auth/token",                       PostToken,           HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "auth/server",                      GetAuthServer,       HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "programs",                         GetPrograms,         HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "programs/{programID}",             GetProgram,          HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "events",                           GetEvents,           HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "events/{eventID}",                 GetEvent,            HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "vens",                             GetVENs,             HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "vens/{venID}",                     GetVEN,              HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "resources",                        GetResources,        HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "resources/{resourceID}",           GetResource,         HTTPMethod.GET);

            AddHandler(HTTPPath.Root + "reports",                          GetReports,          HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "reports",                          PostReport,          HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "reports/{reportID}",               GetReport,           HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "reports/{reportID}",               PutReport,           HTTPMethod.PUT);
            AddHandler(HTTPPath.Root + "reports/{reportID}",               DeleteReport,        HTTPMethod.DELETE);

            AddHandler(HTTPPath.Root + "subscriptions",                    GetSubscriptions,    HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "subscriptions",                    PostSubscription,    HTTPMethod.POST);
            AddHandler(HTTPPath.Root + "subscriptions/{subscriptionID}",   GetSubscription,     HTTPMethod.GET);
            AddHandler(HTTPPath.Root + "subscriptions/{subscriptionID}",   PutSubscription,     HTTPMethod.PUT);
            AddHandler(HTTPPath.Root + "subscriptions/{subscriptionID}",   DeleteSubscription,  HTTPMethod.DELETE);

            AddHandler(HTTPPath.Root + "notifiers",                        GetNotifiers,        HTTPMethod.GET);

            // What only the grid operator does: a VEN that tries is told so,
            // rather than that there is no such thing.
            foreach (var path in new[] { "programs", "programs/{programID}", "events", "events/{eventID}", "vens", "vens/{venID}", "resources", "resources/{resourceID}" })
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
        /// <remarks>
        /// OpenADR 3.1 answers a bad request here with a problem, no longer
        /// with the error of OAuth; and credentials no local controller signs
        /// in with with a 401.
        /// </remarks>
        private Task<HTTPResponse> PostToken(HTTPRequest Request)
        {

            var (clientId, clientSecret, grantType) = CredentialsOf(Request);

            if (grantType is not null && grantType != "client_credentials")
                return Task.FromResult(Problem(Request, HTTPStatusCode.BadRequest, "Bad request", "Only the grant type 'client_credentials' is taken."));

            if (String.IsNullOrEmpty(clientId) || String.IsNullOrEmpty(clientSecret))
                return Task.FromResult(Problem(Request, HTTPStatusCode.BadRequest, "Bad request", "Say who you are: client_id and client_secret, as a form, as HTTP Basic or as JSON."));

            var issued = GridOperator.OpenADR.TryIssueToken(clientId, clientSecret);

            if (issued is not (String token, TimeSpan expiresIn))
            {
                GridOperator.Log.Warning($"A local controller signing in at the OpenADR VTN as '{clientId}' was refused: no such local controller, or not its secret.", "openadr", "auth");
                return Task.FromResult(Problem(Request, HTTPStatusCode.Unauthorized, "Unauthorized", "No local controller signs in with these client credentials."));
            }

            GridOperator.Log.Info($"The local controller '{clientId}' signed in at the OpenADR VTN.", "openadr", "auth");

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JObject(
                           new JProperty("access_token",  token),
                           new JProperty("token_type",    "Bearer"),
                           new JProperty("expires_in",    (Int64) expiresIn.TotalSeconds),
                           new JProperty("scope",         VENScopes)
                       ))
                   );

        }


        /// <summary>
        /// GET auth/server: where the token is had - required of a VTN since
        /// OpenADR 3.1.
        /// </summary>
        private Task<HTTPResponse> GetAuthServer(HTTPRequest Request)

            => Task.FromResult(
                   JSON(Request, HTTPStatusCode.OK, new JObject(
                       new JProperty("tokenURL", GridOperator.OpenADRTokenURLFor(Request))
                   ))
               );

        #endregion

        #region (private) GetPrograms(Request) / GetProgram(Request)

        /// <summary>
        /// GET programs: the program of this grid operator. It has no targets,
        /// so every VEN reads it, whatever targets it asks for.
        /// </summary>
        private Task<HTTPResponse> GetPrograms(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            if (!TryReadWith(Request, login, out _, out var badTargets))
                return Task.FromResult(badTargets);

            var programs = new[] { GridOperator.Program }.
                               Where(program => Request.QueryString.GetString("programName") is not String name ||
                                                program.ProgramName == name).
                               ToArray();

            if (!TryPage(Request, programs, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(_ => GridOperator.ProgramJSON())))
                   );

        }

        /// <summary>
        /// GET programs/{programID}: the program of this grid operator.
        /// </summary>
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
        /// GET events: the limits this local controller may read, the
        /// earliest first, each with only the targets it may see.
        /// </summary>
        /// <remarks>
        /// "?active=true" leaves out those that are over, as OpenADR 3.1 has
        /// it; without it, they are listed, too.
        /// </remarks>
        private Task<HTTPResponse> GetEvents(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            if (!TryReadWith(Request, login, out var readWith, out var badTargets))
                return Task.FromResult(badTargets);

            var programId  = Request.QueryString.GetString("programID");
            var active     = Request.QueryString.GetBoolean("active") == true;
            var now        = GridOperator.TimeProvider.GetUtcNow();

            var visible    = GridOperator.OpenADR.Events().
                                          Where  (gridEvent => gridEvent.IsVisibleTo(readWith)).
                                          Where  (gridEvent => programId is null || gridEvent.Event.ProgramId.ToString() == programId).
                                          Where  (gridEvent => !active || gridEvent.End > now).
                                          OrderBy(gridEvent => gridEvent.Start).
                                          ToArray();

            if (!TryPage(Request, visible, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(gridEvent => gridEvent.EventJSON(readWith))))
                   );

        }

        /// <summary>
        /// GET events/{eventID}: one limit this local controller may read.
        /// </summary>
        private Task<HTTPResponse> GetEvent(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            if (!TryReadWith(Request, login, out var readWith, out var badTargets))
                return Task.FromResult(badTargets);

            var id         = IdOf(Request);
            var gridEvent  = GridOperator.OpenADR.Event(id);

            // One this local controller may not read is none of its business,
            // not even that it exists.
            return Task.FromResult(
                       gridEvent is not null && gridEvent.IsVisibleTo(readWith)
                           ? JSON   (Request, HTTPStatusCode.OK, gridEvent.EventJSON(readWith))
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no event '{id}' for '{login.VENName}'.")
                   );

        }

        #endregion

        #region (private) GetVENs(Request) / GetVEN(Request)

        /// <summary>
        /// GET vens: the ven of this local controller, and nobody else's -
        /// its clientID is its own - unless "?venName" or "?targets" leave it out.
        /// </summary>
        private Task<HTTPResponse> GetVENs(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var venName  = Request.QueryString.GetString("venName");
            var targets  = Request.QueryString.GetStrings(OpenADRTargets.PropertyName).ToArray();

            var own      = new[] { login }.
                               Where(own => venName is null || own.VENName.Equals(venName, StringComparison.OrdinalIgnoreCase)).
                               Where(own => targets.Length == 0 || own.GrantedTargets.Intersect(targets, StringComparer.Ordinal).Any()).
                               ToArray();

            if (!TryPage(Request, own, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(VENJSON)))
                   );

        }

        /// <summary>
        /// GET vens/{venID}: the ven of this local controller.
        /// </summary>
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

        #region (private) GetResources(Request) / GetResource(Request)

        /// <summary>
        /// GET resources: none. This grid operator grants its targets to the
        /// ven of a local controller, and keeps no resources of it.
        /// </summary>
        private Task<HTTPResponse> GetResources(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray())
                   );

        }

        /// <summary>
        /// GET resources/{resourceID}: there is none.
        /// </summary>
        private Task<HTTPResponse> GetResource(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no resource '{IdOf(Request)}'.")
                   );

        }

        #endregion

        #region (private) GetReports(Request) / GetReport(Request) / PostReport(Request) / PutReport(Request) / DeleteReport(Request)

        /// <summary>
        /// GET reports: the reports of this local controller - those of its
        /// clientID - the newest first, with "?eventID", "?programID" and
        /// "?clientName".
        /// </summary>
        private Task<HTTPResponse> GetReports(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var eventId     = Request.QueryString.GetString("eventID");
            var programId   = Request.QueryString.GetString("programID");
            var clientName  = Request.QueryString.GetString("clientName");

            var own = GridOperator.OpenADR.Reports().
                                   Where(report => report.ClientID == login.ClientID).
                                   Where(report => eventId    is null || report.EventId.ToString() == eventId).
                                   // A report of 3.1 has no programID; its event has.
                                   Where(report => programId  is null || GridOperator.OpenADR.Event(report.EventId.ToString())?.Event.ProgramId.ToString() == programId).
                                   Where(report => clientName is null || report.ClientName == clientName).
                                   ToArray();

            if (!TryPage(Request, own, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(ReportJSON)))
                   );

        }

        /// <summary>
        /// GET reports/{reportID}: one report of this local controller.
        /// </summary>
        private Task<HTTPResponse> GetReport(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id      = IdOf(Request);
            var report  = GridOperator.OpenADR.Reports().
                                       FirstOrDefault(report => report.Id?.ToString() == id &&
                                                                report.ClientID       == login.ClientID);

            return Task.FromResult(
                       report is not null
                           ? JSON   (Request, HTTPStatusCode.OK, ReportJSON(report))
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no report '{id}' of '{login.VENName}'.")
                   );

        }

        /// <summary>
        /// POST reports: a report of this local controller, on an event it may
        /// read. The VTN writes its clientID into it, whatever it says.
        /// </summary>
        private Task<HTTPResponse> PostReport(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            if (!TryReadReport(Request, login, out var report, out var badReport))
                return Task.FromResult(badReport);

            var now   = GridOperator.TimeProvider.GetUtcNow();
            var kept  = new Report(report.EventId,
                                   report.ClientName,
                                   report.Resources,
                                   report.ReportName,
                                   report.PayloadDescriptors,
                                   login.ClientID,
                                   Report_Id.NewRandom,
                                   now,
                                   now);

            GridOperator.OpenADR.AddReport(kept);

            GridOperator.Log.Info($"The local controller '{login.VENName}' reported on the event '{report.EventId}'" +
                                  (report.ReportName is not null ? $": {report.ReportName}." : "."),
                                  "openadr", "report");

            GridOperator.NotifyOf(ObjectType.REPORT, OpenADROperation.CREATE, kept, login.ClientID);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.Created, ReportJSON(kept))
                   );

        }

        /// <summary>
        /// PUT reports/{reportID}: a changed report of this local controller.
        /// </summary>
        private Task<HTTPResponse> PutReport(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id        = IdOf(Request);
            var existing  = GridOperator.OpenADR.Reports().
                                         FirstOrDefault(report => report.Id?.ToString() == id &&
                                                                  report.ClientID       == login.ClientID);

            if (existing is null)
                return Task.FromResult(Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no report '{id}' of '{login.VENName}'."));

            if (!TryReadReport(Request, login, out var report, out var badReport))
                return Task.FromResult(badReport);

            var changed = new Report(report.EventId,
                                     report.ClientName,
                                     report.Resources,
                                     report.ReportName,
                                     report.PayloadDescriptors,
                                     login.ClientID,
                                     existing.Id,
                                     existing.Created,
                                     GridOperator.TimeProvider.GetUtcNow());

            if (GridOperator.OpenADR.TryReplaceReport(changed) is null)
                return Task.FromResult(Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no report '{id}' of '{login.VENName}'."));

            GridOperator.NotifyOf(ObjectType.REPORT, OpenADROperation.UPDATE, changed, login.ClientID);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, ReportJSON(changed))
                   );

        }

        /// <summary>
        /// DELETE reports/{reportID}: a report of this local controller, removed.
        /// </summary>
        private Task<HTTPResponse> DeleteReport(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id = IdOf(Request);

            if (GridOperator.OpenADR.TryRemoveReport(id, login.ClientID) is not Report removed)
                return Task.FromResult(Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no report '{id}' of '{login.VENName}'."));

            GridOperator.NotifyOf(ObjectType.REPORT, OpenADROperation.DELETE, removed, login.ClientID);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, ReportJSON(removed))
                   );

        }

        #endregion

        #region (private) GetSubscriptions(Request) / GetSubscription(Request) / PostSubscription(Request) / PutSubscription(Request) / DeleteSubscription(Request)

        /// <summary>
        /// GET subscriptions: the subscriptions of this local controller, with
        /// "?programID", "?clientName" and "?objects".
        /// </summary>
        private Task<HTTPResponse> GetSubscriptions(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var programId   = Request.QueryString.GetString("programID");
            var clientName  = Request.QueryString.GetString("clientName");
            var objects     = Request.QueryString.GetStrings("objects").ToArray();

            var own = GridOperator.Subscriptions.Of(login.ClientID).
                                   Where(subscription => programId  is null || subscription.ProgramId?.ToString() == programId).
                                   Where(subscription => clientName is null || subscription.ClientName == clientName).
                                   Where(subscription => objects.Length == 0 ||
                                                         subscription.ObjectOperations.Any(objectOperation => objectOperation.Objects.Any(@object => objects.Contains(@object.ToString(), StringComparer.OrdinalIgnoreCase)))).
                                   ToArray();

            if (!TryPage(Request, own, out var page, out var badPage))
                return Task.FromResult(badPage);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new JArray(page.Select(subscription => subscription.ToJSON())))
                   );

        }

        /// <summary>
        /// GET subscriptions/{subscriptionID}: one subscription of this local controller.
        /// </summary>
        private Task<HTTPResponse> GetSubscription(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id = IdOf(Request);

            return Task.FromResult(
                       GridOperator.Subscriptions.Get(id, login.ClientID) is Subscription subscription
                           ? JSON   (Request, HTTPStatusCode.OK, subscription.ToJSON())
                           : Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no subscription '{id}' of '{login.VENName}'.")
                   );

        }

        /// <summary>
        /// POST subscriptions: a subscription of this local controller, whose
        /// callbacks answered their check. The VTN writes its clientID into it.
        /// </summary>
        private async Task<HTTPResponse> PostSubscription(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return refused!;

            var (subscription, badSubscription) = await TryReadSubscription(Request, login);

            if (subscription is null)
                return badSubscription!;

            var now   = GridOperator.TimeProvider.GetUtcNow();
            var kept  = new Subscription(subscription.ClientName,
                                         subscription.ObjectOperations,
                                         subscription.ProgramId,
                                         subscription.Targets,
                                         login.ClientID,
                                         Subscription_Id.NewRandom,
                                         now,
                                         now);

            if (!GridOperator.Subscriptions.TryAdd(kept, out var error))
                return Problem(Request, HTTPStatusCode.Conflict, "Conflict", error);

            GridOperator.Subscriptions.DeliverOverConnection(kept.Id?.ToString() ?? "", ConnectionToTellOver(Request, kept));

            GridOperator.Log.Info($"The local controller '{login.VENName}' subscribed to {String.Join(", ", kept.ObjectOperations.Select(objectOperation => $"{String.Join("/", objectOperation.Operations)} {String.Join("/", objectOperation.Objects)}"))}: subscription '{kept.Id}'.",
                                  "openadr", "subscription");

            GridOperator.NotifyOf(ObjectType.SUBSCRIPTION, OpenADROperation.CREATE, kept, login.ClientID);

            return JSON(Request, HTTPStatusCode.Created, kept.ToJSON());

        }

        /// <summary>
        /// PUT subscriptions/{subscriptionID}: a changed subscription of this
        /// local controller, whose callbacks answered their check.
        /// </summary>
        private async Task<HTTPResponse> PutSubscription(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return refused!;

            var id = IdOf(Request);

            if (GridOperator.Subscriptions.Get(id, login.ClientID) is not Subscription existing)
                return Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no subscription '{id}' of '{login.VENName}'.");

            var (subscription, badSubscription) = await TryReadSubscription(Request, login);

            if (subscription is null)
                return badSubscription!;

            var changed = new Subscription(subscription.ClientName,
                                           subscription.ObjectOperations,
                                           subscription.ProgramId,
                                           subscription.Targets,
                                           login.ClientID,
                                           existing.Id,
                                           existing.Created,
                                           GridOperator.TimeProvider.GetUtcNow());

            if (GridOperator.Subscriptions.TryReplace(changed) is null)
                return Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no subscription '{id}' of '{login.VENName}'.");

            GridOperator.Subscriptions.DeliverOverConnection(id, ConnectionToTellOver(Request, changed));

            GridOperator.NotifyOf(ObjectType.SUBSCRIPTION, OpenADROperation.UPDATE, changed, login.ClientID);

            return JSON(Request, HTTPStatusCode.OK, changed.ToJSON());

        }

        /// <summary>
        /// DELETE subscriptions/{subscriptionID}: a subscription of this local
        /// controller, removed.
        /// </summary>
        private Task<HTTPResponse> DeleteSubscription(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is not LocalControllerLogin login)
                return Task.FromResult(refused!);

            var id = IdOf(Request);

            if (GridOperator.Subscriptions.TryRemove(id, login.ClientID) is not Subscription removed)
                return Task.FromResult(Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no subscription '{id}' of '{login.VENName}'."));

            GridOperator.Log.Info($"The local controller '{login.VENName}' removed its subscription '{id}'.", "openadr", "subscription");

            // Told before it is gone from the matching would be told itself;
            // it is gone, so whoever else of the VEN listens is told.
            GridOperator.NotifyOf(ObjectType.SUBSCRIPTION, OpenADROperation.DELETE, removed, login.ClientID);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, removed.ToJSON())
                   );

        }

        #endregion

        #region (private) GetNotifiers(Request)

        /// <summary>
        /// GET notifiers: how this VTN tells of changes - by webhooks, and not
        /// by MQTT, for which it has no broker.
        /// </summary>
        /// <remarks>
        /// OpenADR 3.1 gives this the scope read_all, which it also calls the
        /// business logic's, while its User Guide has a VEN ask it. Any VEN
        /// signed in asks it here.
        /// </remarks>
        private Task<HTTPResponse> GetNotifiers(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       JSON(Request, HTTPStatusCode.OK, new NotifiersResponse(WebHook: true).ToJSON())
                   );

        }

        #endregion

        #region (private) Forbidden(Request) / UnknownPath(Request)

        /// <summary>
        /// A POST, PUT or DELETE of a program, an event, a VEN or a resource,
        /// which only the grid operator makes.
        /// </summary>
        private Task<HTTPResponse> Forbidden(HTTPRequest Request)
        {

            if (SignedIn(Request, out var refused) is null)
                return Task.FromResult(refused!);

            return Task.FromResult(
                       Problem(Request, HTTPStatusCode.Forbidden, "Forbidden",
                               "Programs, events, VENs and resources are made by the grid operator; a VEN reads them, and writes its reports and its subscriptions.")
                   );

        }

        /// <summary>
        /// Anything else below the VTN: a 404 problem.
        /// </summary>
        private Task<HTTPResponse> UnknownPath(HTTPRequest Request)

            => Task.FromResult(
                   Problem(Request, HTTPStatusCode.NotFound, "Not found", $"The VTN has nothing at '{Request.Path}'.")
               );

        #endregion

        #region (private) TryReadWith(Request, Login, out ReadWith, out BadTargets)

        /// <summary>
        /// The targets a VEN reads programs and events with, as OpenADR 3.1
        /// has it: those granted to it, met with those it asks for by
        /// "?targets" - never beyond those granted, and nothing where they do
        /// not meet.
        /// </summary>
        private static Boolean TryReadWith(HTTPRequest                  Request,
                                           LocalControllerLogin         Login,
                                           out IReadOnlyList<String>    ReadWith,
                                           out HTTPResponse             BadTargets)
        {

            var asked = Request.QueryString.GetStrings(OpenADRTargets.PropertyName).ToArray();

            if (asked.Any(target => !OpenADRTargets.IsValid(target)))
            {
                ReadWith    = [];
                BadTargets  = Problem(Request, HTTPStatusCode.BadRequest, "Bad request", $"Every one of the '{OpenADRTargets.PropertyName}' is a string of {OpenADRTargets.MinLength} to {OpenADRTargets.MaxLength} characters.");
                return false;
            }

            ReadWith    = asked.Length == 0
                              ? Login.GrantedTargets
                              : [ .. Login.GrantedTargets.Intersect(asked, StringComparer.Ordinal) ];
            BadTargets  = null!;
            return true;

        }

        #endregion

        #region (private) TryReadReport(Request, Login, out Report, out BadReport)

        /// <summary>
        /// The report in the body of the request, on an event the VEN may read.
        /// </summary>
        private Boolean TryReadReport(HTTPRequest           Request,
                                      LocalControllerLogin  Login,
                                      out Report            Report,
                                      out HTTPResponse      BadReport)
        {

            Report     = null!;
            BadReport  = null!;

            JObject json;

            try
            {
                json = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "");
            }
            catch (Exception e)
            {
                BadReport = Problem(Request, HTTPStatusCode.BadRequest, "Bad request", $"The report is no JSON object: {e.Message}");
                return false;
            }

            if (!Report.TryParse(json, out var report, out var error))
            {
                BadReport = Problem(Request, HTTPStatusCode.BadRequest, "Bad request", error);
                return false;
            }

            var gridEvent = GridOperator.OpenADR.Event(report.EventId.ToString());

            if (gridEvent is null || !gridEvent.IsVisibleTo(Login.GrantedTargets))
            {
                BadReport = Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no event '{report.EventId}' for '{Login.VENName}' to report on.");
                return false;
            }

            Report = report;
            return true;

        }

        #endregion

        #region (private) TryReadSubscription(Request, Login)

        /// <summary>
        /// The subscription in the body of the request, whose callbacks are
        /// taken and answered their check - or the problem that says why not.
        /// </summary>
        private async Task<(Subscription? Subscription, HTTPResponse? BadSubscription)> TryReadSubscription(HTTPRequest           Request,
                                                                                                            LocalControllerLogin  Login)
        {

            JObject json;

            try
            {
                json = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "");
            }
            catch (Exception e)
            {
                return (null, Problem(Request, HTTPStatusCode.BadRequest, "Bad request", $"The subscription is no JSON object: {e.Message}"));
            }

            if (!Subscription.TryParse(json, out var subscription, out var error))
                return (null, Problem(Request, HTTPStatusCode.BadRequest, "Bad request", error));

            if (subscription.ProgramId.HasValue && subscription.ProgramId != GridOperator.Program.Id)
                return (null, Problem(Request, HTTPStatusCode.NotFound, "Not found", $"There is no program '{subscription.ProgramId}' to subscribe to."));

            // Plain HTTP only from a VEN that came over plain HTTP itself: one
            // that came over WebSocket or SSH is asked for HTTPS.
            var allowHTTP     = Request.NetworkStream is not VENTunnelStream &&
                               !GridOperator.ReachedOverHTTPS(Request);

            var overMessages  = Request.NetworkStream is VENTunnelStream { ViaMessages: true };

            foreach (var objectOperation in subscription.ObjectOperations)
            {

                var callback = objectOperation.CallbackURL.ToString();

                // Over the JSON messages of OpenADR, a callback of the kind of
                // the connection - ws://, wss://, ssh:// - is the connection
                // itself: told by Notify over it, with nothing to check. Over
                // the REST API it is no callback at all.
                if (overMessages && OpenADRSubscriptions.IsConnectionCallback(callback))
                    continue;

                if (!WebhookNotifier.IsAcceptableCallback(callback, allowHTTP, out var notAcceptable))
                    return (null, Problem(Request, HTTPStatusCode.BadRequest, "Bad request", notAcceptable));

                if (await GridOperator.Webhooks.VerifyAsync(callback, objectOperation.BearerToken) is String notVerified)
                {
                    GridOperator.Log.Warning($"The local controller '{Login.VENName}' was not let subscribe: {notVerified}", "openadr", "subscription");
                    return (null, Problem(Request, HTTPStatusCode.BadRequest, "Bad request", notVerified));
                }

            }

            return (subscription, null);

        }

        #endregion

        #region (private static) ConnectionToTellOver(Request, Subscription)

        /// <summary>
        /// The connection of the JSON messages of OpenADR the given subscription
        /// is told over: the one the request came over, where it came over one
        /// and a callback of it is that connection - else none.
        /// </summary>
        private static String? ConnectionToTellOver(HTTPRequest   Request,
                                                    Subscription  Subscription)

            => Request.NetworkStream is VENTunnelStream { ViaMessages: true } tunnel &&
               Subscription.ObjectOperations.Any(objectOperation => OpenADRSubscriptions.IsConnectionCallback(objectOperation.CallbackURL.ToString()))
                   ? tunnel.ConnectionId
                   : null;

        #endregion

        #region (private) SignedIn(Request, out Refused)

        /// <summary>
        /// The local controller behind the bearer token of the request - or,
        /// for a request that came through a tunnel of the REST API, the VEN
        /// the connection of the tunnel names - or null, and the 401 that says
        /// why.
        /// </summary>
        /// <remarks>
        /// A request through a tunnel is the VEN of its connection's, and of
        /// nobody else: a bearer token in it is not looked at, so that a VEN
        /// signed in over a WebSocket or over SSH does not become another one
        /// by a token it holds.
        /// </remarks>
        private LocalControllerLogin? SignedIn(HTTPRequest        Request,
                                               out HTTPResponse?  Refused)
        {

            if (Request.NetworkStream is VENTunnelStream tunnel)
            {

                if (GridOperator.OpenADR.Login(tunnel.VENName) is LocalControllerLogin ofTheTunnel)
                {
                    Refused = null;
                    return ofTheTunnel;
                }

                Refused = Problem(Request, HTTPStatusCode.Unauthorized, "Unauthorized",
                                  $"The connection '{tunnel.Login}' came in under is taken as the VEN '{tunnel.VENName}', which is no local controller of this grid operator.");

                return null;

            }

            var authorization = Request.GetHeaderField("Authorization") ?? "";

            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                GridOperator.OpenADR.LoginOfToken(authorization[7..].Trim()) is LocalControllerLogin login)
            {
                Refused = null;
                return login;
            }

            Refused = Problem(Request, HTTPStatusCode.Unauthorized, "Unauthorized",
                              $"Sign in first: a bearer token from {GridOperator.OpenADRTokenURLFor(Request)}, for the client credentials of a local controller.");

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
