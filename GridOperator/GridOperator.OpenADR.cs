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

using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.OpenADRv3;

using cloud.charging.open.GridOperator.OpenADR;

using POIChargingPoolId = cloud.charging.open.protocols.WWCP.POI.ChargingPool_Id;
using GridEventType     = cloud.charging.open.GridOperator.OpenADR.GridEvent;

#endregion

namespace cloud.charging.open.GridOperator
{

    public partial class GridOperator
    {

        #region Data

        /// <summary>
        /// The identification of the one program of this grid operator.
        /// </summary>
        public const String ProgramName = "grid-capacity";

        /// <summary>
        /// The longest name or description of a local controller or a limit.
        /// </summary>
        public const Int32  MaxTextLength = 200;

        #endregion

        #region Properties

        /// <summary>
        /// What the OpenADR 3 VTN keeps: the local controllers, the limits, the reports and the tokens.
        /// </summary>
        public OpenADRStore  OpenADR         { get; }

        /// <summary>
        /// The one program of this grid operator, which every limit is of: the
        /// capacity of its grid, IMPORT_CAPACITY_LIMIT and EXPORT_CAPACITY_LIMIT in kW.
        /// </summary>
        public Program       Program         { get; }

        /// <summary>
        /// Where the OpenADR 3 VTN is, with a slash at its end.
        /// </summary>
        public String        OpenADRVTNURL
            => $"{WebInterfaceURL.ToString().TrimEnd('/')}/{OpenADRVTNAPI.PathSegment}/";

        /// <summary>
        /// Where a local controller gets its token.
        /// </summary>
        public String        OpenADRTokenURL
            => OpenADRVTNURL + "auth/token";

        #endregion


        #region (private) NewProgram()

        /// <summary>
        /// The program, the same at every start: of this grid operator, since it was made.
        /// </summary>
        private Program NewProgram()

            => new (ProgramName,
                    ProgramLongName:     $"The grid capacity of the grid operator '{Own.Id}'",
                    RetailerName:        Own.Id.ToString(),
                    RetailerLongName:    Own.Entity.Name?.FirstText(),
                    PayloadDescriptors:  GridEvent.Kinds.Select(kind => new EventPayloadDescriptor(PayloadType.Parse(kind), UnitType.Parse(GridEvent.Unit))),
                    Id:                  Program_Id.Parse(ProgramName),
                    Created:             Own.Entity.Created,
                    LastModification:    Own.Entity.Created);

        #endregion

        #region ProgramJSON()

        /// <summary>
        /// The program, as OpenADR 3 writes it.
        /// </summary>
        public JObject ProgramJSON()
        {
            var json = Program.ToJSON();
            json["objectType"] = "PROGRAM";
            return json;
        }

        #endregion


        #region LocalControllersAt(GridConnectionPointId)

        /// <summary>
        /// The local controllers of the pools connected at the given grid connection point.
        /// </summary>
        public IReadOnlyList<LocalControllerLogin> LocalControllersAt(String GridConnectionPointId)

            => [ .. OpenADR.Logins().Where(login => login.GridConnectionPointId.Equals(GridConnectionPointId, StringComparison.OrdinalIgnoreCase)) ];

        #endregion

        #region LocalControllerJSON(Login)

        /// <summary>
        /// A local controller, as the JSON API shows it: without its secret,
        /// with when it was last seen and the limits for it that are not over.
        /// </summary>
        public JObject LocalControllerJSON(LocalControllerLogin Login)
        {

            var now   = TimeProvider.GetUtcNow();
            var json  = Login.ToJSON(IncludeSecret: false);

            json.Add(new JProperty("lastSeen",  OpenADR.LastSeen(Login.VENName)?.ToISO8601()));
            json.Add(new JProperty("limits",    new JArray(OpenADR.Events().
                                                                   Where (gridEvent => gridEvent.IsFor(Login.VENName) && gridEvent.End > now).
                                                                   Select(gridEvent => gridEvent.Id.ToString()))));
            json.Add(new JProperty("reports",   OpenADR.Reports().Count(report => report.ClientName.Equals(Login.VENName, StringComparison.OrdinalIgnoreCase))));

            return json;

        }

        #endregion

        #region OpenADRJSON()

        /// <summary>
        /// What the OpenADR page leads with: where the VTN is, its program, and how much it has.
        /// </summary>
        public JObject OpenADRJSON()
        {

            var now = TimeProvider.GetUtcNow();

            return new JObject(
                       new JProperty("vtnURL",            OpenADRVTNURL),
                       new JProperty("tokenURL",          OpenADRTokenURL),
                       new JProperty("program",           ProgramJSON()),
                       new JProperty("kinds",             new JArray(GridEvent.Kinds)),
                       new JProperty("unit",              GridEvent.Unit),
                       new JProperty("maxDurationMinutes", (Int64) GridEventType.MaxDuration.TotalMinutes),
                       new JProperty("localControllers",  OpenADR.Logins().Count),
                       new JProperty("signedIn",          OpenADR.ActiveTokens()),
                       new JProperty("activeLimits",      OpenADR.Events().Count(gridEvent => gridEvent.StateAt(now) == "active")),
                       new JProperty("scheduledLimits",   OpenADR.Events().Count(gridEvent => gridEvent.StateAt(now) == "scheduled")),
                       new JProperty("reports",           OpenADR.Reports().Count),
                       new JProperty("file",              OpenADR.FilePath)
                   );

        }

        #endregion


        #region TryAddLocalController(JSON, Who, out Login, out Secret, out Error, out Refusal)

        /// <summary>
        /// Let a local controller sign in at the VTN: { "venName",
        /// "chargingPoolId", "gridConnectionPointId", "description" }. Its
        /// secret is made now, said once, and not kept.
        /// </summary>
        public Boolean TryAddLocalController(JObject                                          JSON,
                                             String                                           Who,
                                             [NotNullWhen(true)]  out LocalControllerLogin?  Login,
                                             [NotNullWhen(true)]  out String?                Secret,
                                             [NotNullWhen(false)] out String?                Error,
                                             out ChangeRefusal                                Refusal)
        {

            Login    = null;
            Secret   = null;
            Refusal  = ChangeRefusal.Invalid;

            var venName      = JSON.Value<String>("venName")?.              Trim() ?? "";
            var poolId       = JSON.Value<String>("chargingPoolId")?.       Trim() ?? "";
            var pointId      = JSON.Value<String>("gridConnectionPointId")?.Trim() ?? "";
            var description  = JSON.Value<String>("description")?.          Trim();

            if (!LocalControllerLogin.IsAcceptableName(venName, out Error))
                return false;

            if (!POIChargingPoolId.TryParse(poolId, out var chargingPoolId))
            {
                Error = $"'{poolId}' is no identification of a charging pool, as POI writes one: e.g. 'DE*GEF*P1'.";
                return false;
            }

            if (!TryGetGridConnectionPoint(pointId, out var point))
            {
                Error = $"The grid operator '{Own.Id}' has no grid connection point '{pointId}' for the pool '{chargingPoolId}' to be connected at.";
                return false;
            }

            if (description is { Length: > MaxTextLength })
            {
                Error = $"The description is {description.Length} characters long; {MaxTextLength} are the most taken.";
                return false;
            }

            if (OpenADR.Login(venName) is not null)
            {
                Error    = $"There is a local controller '{venName}' already.";
                Refusal  = ChangeRefusal.Conflict;
                return false;
            }

            var login = LocalControllerLogin.New(venName,
                                                 chargingPoolId.ToString(),
                                                 point.Id.ToString(),
                                                 String.IsNullOrEmpty(description) ? null : description,
                                                 TimeProvider.GetUtcNow(),
                                                 out var secret);

            if (!OpenADR.TrySetLogin(login, out Error))
            {
                Refusal = ChangeRefusal.NotSaved;
                return false;
            }

            Log.Notice($"'{Who}' let the local controller '{venName}' of the charging pool '{chargingPoolId}', behind the grid connection point '{point.Id}', sign in at the OpenADR VTN.",
                       "openadr", "web");

            Login   = login;
            Secret  = secret;
            return true;

        }

        #endregion

        #region TryNewSecret(VENName, Who, out Login, out Secret, out Error, out Refusal)

        /// <summary>
        /// A new secret for a local controller: the old one signs in no more,
        /// and neither does any token handed out for it.
        /// </summary>
        public Boolean TryNewSecret(String                                           VENName,
                                    String                                           Who,
                                    [NotNullWhen(true)]  out LocalControllerLogin?  Login,
                                    [NotNullWhen(true)]  out String?                Secret,
                                    [NotNullWhen(false)] out String?                Error,
                                    out ChangeRefusal                                Refusal)
        {

            Login   = null;
            Secret  = null;

            if (OpenADR.Login(VENName) is not LocalControllerLogin existing)
            {
                Error    = $"There is no local controller '{VENName}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            var renewed = existing.WithNewSecret(out var secret);

            if (!OpenADR.TrySetLogin(renewed, out Error))
            {
                Refusal = ChangeRefusal.NotSaved;
                return false;
            }

            Log.Notice($"'{Who}' gave the local controller '{existing.VENName}' a new secret: the old one signs in no more.", "openadr", "web", "security");

            Login    = renewed;
            Secret   = secret;
            Refusal  = ChangeRefusal.Invalid;
            return true;

        }

        #endregion

        #region TryRemoveLocalController(VENName, Who, out Error, out Refusal)

        /// <summary>
        /// Let a local controller sign in no more.
        /// </summary>
        public Boolean TryRemoveLocalController(String                            VENName,
                                                String                            Who,
                                                [NotNullWhen(false)] out String?  Error,
                                                out ChangeRefusal                 Refusal)
        {

            if (OpenADR.Login(VENName) is not LocalControllerLogin existing)
            {
                Error    = $"There is no local controller '{VENName}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            if (!OpenADR.TryRemoveLogin(existing.VENName, out Error))
            {
                Refusal = ChangeRefusal.NotSaved;
                return false;
            }

            Log.Notice($"'{Who}' removed the local controller '{existing.VENName}': it signs in at the OpenADR VTN no more.", "openadr", "web");

            Refusal = ChangeRefusal.Invalid;
            return true;

        }

        #endregion


        #region TrySendLimit(JSON, Who, out GridEvent, out Error, out Refusal)

        /// <summary>
        /// Send a limit to the local controllers behind a grid connection
        /// point, or to all of them: { "kind", "limitKW", "start",
        /// "durationMinutes", "gridConnectionPointId", "name", "priority" } -
        /// "start" absent for now, "gridConnectionPointId" absent for all.
        /// </summary>
        public Boolean TrySendLimit(JObject                               JSON,
                                    String                                Who,
                                    [NotNullWhen(true)]  out GridEvent?  GridEvent,
                                    [NotNullWhen(false)] out String?     Error,
                                    out ChangeRefusal                     Refusal)
        {

            GridEvent  = null;
            Refusal    = ChangeRefusal.Invalid;

            var now    = TimeProvider.GetUtcNow();

            #region What kind of limit, how much and when

            var kind = JSON.Value<String>("kind")?.Trim() ?? GridEventType.Kinds[0];

            if (!GridEventType.Kinds.Contains(kind))
            {
                Error = $"A limit is one of {String.Join(", ", GridEventType.Kinds)}, not '{kind}'.";
                return false;
            }

            if (JSON["limitKW"] is not JValue { Type: JTokenType.Integer or JTokenType.Float } limitJSON ||
                limitJSON.Value<Double>() is var limitKW && (Double.IsNaN(limitKW) || limitKW < 0 || limitKW > 1_000_000))
            {
                Error = "'limitKW' is the limit in kW, a number from 0 to 1000000.";
                return false;
            }

            var start = now;

            if (JSON["start"] is JToken startJSON && startJSON.Type != JTokenType.Null &&
                !(startJSON.Type == JTokenType.String && String.IsNullOrWhiteSpace(startJSON.Value<String>())))
            {

                if (startJSON.Type == JTokenType.Date)
                    start = startJSON.Value<DateTime>() is var date && date.Kind == DateTimeKind.Unspecified
                                ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
                                : new DateTimeOffset(date);

                else if (startJSON.Type != JTokenType.String || !DateTimeOffset.TryParse(startJSON.Value<String>(), out start))
                {
                    Error = "'start' is when the limit begins, e.g. '2026-10-19T17:00:00+02:00', or absent for now.";
                    return false;
                }

                if (start < now - TimeSpan.FromMinutes(5))
                {
                    Error = $"A limit can not begin in the past: '{start.ToISO8601()}' was before now, '{now.ToISO8601()}'.";
                    return false;
                }

            }

            if (JSON["durationMinutes"] is not JValue { Type: JTokenType.Integer or JTokenType.Float } durationJSON ||
                durationJSON.Value<Double>() is var minutes && (Double.IsNaN(minutes) || minutes < 1 || minutes > GridEventType.MaxDuration.TotalMinutes))
            {
                Error = $"'durationMinutes' is how long the limit lasts, from 1 to {(Int64) GridEventType.MaxDuration.TotalMinutes} minutes.";
                return false;
            }

            UInt32? priority = null;

            if (JSON["priority"] is JToken priorityJSON && priorityJSON.Type != JTokenType.Null)
            {
                if (priorityJSON.Type != JTokenType.Integer || priorityJSON.Value<Int64>() is < 0 or > 1000)
                {
                    Error = "'priority' is a number from 0, the highest, to 1000, or absent.";
                    return false;
                }
                priority = (UInt32) priorityJSON.Value<Int64>();
            }

            var name = JSON.Value<String>("name")?.Trim();

            if (name is { Length: > MaxTextLength })
            {
                Error = $"The name is {name.Length} characters long; {MaxTextLength} are the most taken.";
                return false;
            }

            #endregion

            #region For whom

            String? pointId = null;
            IReadOnlyList<LocalControllerLogin> forWhom;

            var pointText = JSON.Value<String>("gridConnectionPointId")?.Trim();

            if (!String.IsNullOrEmpty(pointText))
            {

                if (!TryGetGridConnectionPoint(pointText, out var point))
                {
                    Error    = $"The grid operator '{Own.Id}' has no grid connection point '{pointText}'.";
                    Refusal  = ChangeRefusal.NotFound;
                    return false;
                }

                pointId  = point.Id.ToString();
                forWhom  = LocalControllersAt(pointId);

            }
            else
                forWhom  = OpenADR.Logins();

            if (forWhom.Count == 0)
            {
                Error    = pointId is not null
                               ? $"There is no local controller behind the grid connection point '{pointId}' to send a limit to."
                               : "There is no local controller to send a limit to.";
                Refusal  = ChangeRefusal.Conflict;
                return false;
            }

            #endregion

            var gridEvent = GridEventType.New(Program.Id!.Value,
                                                  kind,
                                                  limitKW,
                                                  start,
                                                  TimeSpan.FromMinutes(minutes),
                                                  forWhom.Select(login => login.VENName),
                                                  pointId,
                                                  String.IsNullOrEmpty(name) ? null : name,
                                                  priority,
                                                  Who,
                                                  now);

            if (!OpenADR.TryAddEvent(gridEvent, out Error))
            {
                Refusal = ChangeRefusal.NotSaved;
                return false;
            }

            Log.Notice($"'{Who}' sent {kind} {limitKW} kW from {start.ToISO8601()} for {minutes} minute(s) to {forWhom.Count} local controller(s)" +
                       (pointId is not null ? $" behind the grid connection point '{pointId}'" : "") +
                       $": event '{gridEvent.Id}'.",
                       "openadr", "event", "web");

            GridEvent = gridEvent;
            return true;

        }

        #endregion

        #region TryCancelLimit(EventId, Who, out Error, out Refusal)

        /// <summary>
        /// Cancel a limit: the local controllers do not read it any more.
        /// </summary>
        public Boolean TryCancelLimit(String                            EventId,
                                      String                            Who,
                                      [NotNullWhen(false)] out String?  Error,
                                      out ChangeRefusal                 Refusal)
        {

            if (OpenADR.Event(EventId) is not GridEvent gridEvent)
            {
                Error    = $"There is no limit '{EventId}'.";
                Refusal  = ChangeRefusal.NotFound;
                return false;
            }

            if (!OpenADR.TryRemoveEvent(gridEvent.Id, out Error))
            {
                Refusal = ChangeRefusal.NotSaved;
                return false;
            }

            Log.Notice($"'{Who}' cancelled the limit '{gridEvent.Id}', {gridEvent.Kind} {gridEvent.LimitKW} kW, " +
                       $"for {gridEvent.VENNames.Count} local controller(s).",
                       "openadr", "event", "web");

            Refusal = ChangeRefusal.Invalid;
            return true;

        }

        #endregion

        #region LimitsJSON() / ReportsJSON()

        /// <summary>
        /// Every limit, the newest first.
        /// </summary>
        public JObject LimitsJSON()
        {

            var now = TimeProvider.GetUtcNow();

            return new JObject(
                       new JProperty("timestamp",  now.ToISO8601()),
                       new JProperty("limits",     new JArray(OpenADR.Events().Select(gridEvent => gridEvent.ToJSON(now))))
                   );

        }

        /// <summary>
        /// Every report kept, the newest first.
        /// </summary>
        public JObject ReportsJSON()

            => new (new JProperty("capacity",  OpenADRStore.ReportCapacity),
                    new JProperty("reports",   new JArray(OpenADR.Reports().Select(report => {
                                                   var json = report.ToJSON();
                                                   json["objectType"] = "REPORT";
                                                   return json;
                                               }))));

        #endregion

    }

}
