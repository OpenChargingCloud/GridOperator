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

using OpenADRValueType = cloud.charging.open.protocols.OpenADRv3.ValueType;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// A limit this grid operator sent to the local controllers behind one of
    /// its grid connection points - or to all of them: the event of OpenADR 3
    /// they read, and what it was sent for.
    /// </summary>
    /// <remarks>
    /// One interval, as long as the event, with one value: the limit in kW,
    /// of the kind its payload type says - IMPORT_CAPACITY_LIMIT for what the
    /// pool may take from the grid, EXPORT_CAPACITY_LIMIT for what it may feed
    /// into it. Targeted by VEN_NAME at the local controllers it is for, so
    /// that each of them reads only what is meant for it.
    /// </remarks>
    /// <param name="Event">The event, as the local controllers read it.</param>
    /// <param name="GridConnectionPointId">The grid connection point it is for; null for every local controller.</param>
    /// <param name="SentBy">Who sent it.</param>
    public sealed record GridEvent(Event    Event,
                                   String?  GridConnectionPointId,
                                   String   SentBy)
    {

        #region Data

        /// <summary>
        /// The kinds of limit that may be sent.
        /// </summary>
        public static readonly IReadOnlyList<String>  Kinds        = [ "IMPORT_CAPACITY_LIMIT", "EXPORT_CAPACITY_LIMIT" ];

        /// <summary>
        /// The unit of every limit.
        /// </summary>
        public const           String                 Unit         = "KW";

        /// <summary>
        /// The longest event taken: a limit for longer is a new connection agreement, not an event.
        /// </summary>
        public static readonly TimeSpan               MaxDuration  = TimeSpan.FromDays(7);

        #endregion

        #region Properties

        /// <summary>
        /// Its identification.
        /// </summary>
        public Event_Id                Id
            => Event.Id!.Value;

        /// <summary>
        /// What kind of limit it is.
        /// </summary>
        public String                  Kind
            => Event.PayloadDescriptors.FirstOrDefault()?.PayloadType.ToString() ?? "";

        /// <summary>
        /// The limit, in kW.
        /// </summary>
        public Double                  LimitKW
            => Event.Intervals.FirstOrDefault()?.Payloads.FirstOrDefault()?.Values.FirstOrDefault() switch {
                   Double  number  => number,
                   Int64   number  => number,
                   Int32   number  => number,
                   Decimal number  => (Double) number,
                   JValue  value   => value.Value<Double>(),
                   _               => 0
               };

        /// <summary>
        /// When it begins.
        /// </summary>
        public DateTimeOffset          Start
            => Event.IntervalPeriod?.Start ?? Event.Created ?? DateTimeOffset.MinValue;

        /// <summary>
        /// When it ends.
        /// </summary>
        public DateTimeOffset          End
            => Start + (Event.IntervalPeriod?.Duration ?? TimeSpan.Zero);

        /// <summary>
        /// The names of the local controllers it is for.
        /// </summary>
        public IReadOnlyList<String>   VENNames
            => [ .. Event.Targets.Where (target => target.Type.ToString() == LocalControllerLogin.VENNameTarget).
                                  SelectMany(target => target.Values).
                                  Select(value => value?.ToString() ?? "").
                                  Where (name  => name.Length > 0) ];

        #endregion


        #region (static) New(ProgramId, Kind, LimitKW, Start, Duration, VENNames, GridConnectionPointId, Name, Priority, SentBy, Now)

        /// <summary>
        /// A new limit, as the event of OpenADR 3 the local controllers read.
        /// </summary>
        public static GridEvent New(Program_Id              ProgramId,
                                    String                  Kind,
                                    Double                  LimitKW,
                                    DateTimeOffset          Start,
                                    TimeSpan                Duration,
                                    IEnumerable<String>     VENNames,
                                    String?                 GridConnectionPointId,
                                    String?                 Name,
                                    UInt32?                 Priority,
                                    String                  SentBy,
                                    DateTimeOffset          Now)

            => new (new Event(Object_Id.Parse(ProgramId.ToString()),
                              [ new Interval(Interval_Id.Parse(0),
                                             [ new ValuesMap(OpenADRValueType.Parse(Kind), [ LimitKW ]) ]) ],
                              EventName:           Name,
                              Priority:            Priority,
                              Targets:             [ new ValuesMap(OpenADRValueType.Parse(LocalControllerLogin.VENNameTarget), VENNames.Cast<Object>()) ],
                              PayloadDescriptors:  [ new EventPayloadDescriptor(PayloadType.Parse(Kind), UnitType.Parse(Unit)) ],
                              IntervalPeriod:      new IntervalPeriod(Start, Duration),
                              Id:                  Event_Id.NewRandom,
                              Created:             Now,
                              LastModification:    Now),
                    GridConnectionPointId,
                    SentBy);

        #endregion

        #region IsFor(VENName)

        /// <summary>
        /// Whether the given local controller is to read it.
        /// </summary>
        public Boolean IsFor(String VENName)

            => VENNames.Contains(VENName, StringComparer.OrdinalIgnoreCase);

        #endregion

        #region StateAt(Now)

        /// <summary>
        /// "scheduled" before it begins, "active" while it lasts, "over" after.
        /// </summary>
        public String StateAt(DateTimeOffset Now)

            => Now < Start ? "scheduled"
             : Now < End   ? "active"
             :               "over";

        #endregion

        #region EventJSON()

        /// <summary>
        /// The event, as OpenADR 3 writes it.
        /// </summary>
        public JObject EventJSON()
        {
            var json = Event.ToJSON();
            json["objectType"] = "EVENT";
            return json;
        }

        #endregion

        #region ToJSON(Now) / StoredJSON()

        /// <summary>
        /// The limit, as the JSON API shows it: what it is, for whom, and the event.
        /// </summary>
        public JObject ToJSON(DateTimeOffset Now)

            => new (new JProperty("id",                     Id.ToString()),
                    new JProperty("name",                   Event.EventName),
                    new JProperty("kind",                   Kind),
                    new JProperty("limitKW",                LimitKW),
                    new JProperty("start",                  Start.ToISO8601()),
                    new JProperty("end",                    End.  ToISO8601()),
                    new JProperty("state",                  StateAt(Now)),
                    new JProperty("priority",               Event.Priority),
                    new JProperty("gridConnectionPointId",  GridConnectionPointId),
                    new JProperty("localControllers",       new JArray(VENNames)),
                    new JProperty("sentBy",                 SentBy),
                    new JProperty("created",                Event.Created?.ToISO8601()),
                    new JProperty("event",                  EventJSON()));

        /// <summary>
        /// The limit, as it is kept.
        /// </summary>
        public JObject StoredJSON()

            => new (new JProperty("event",                  Event.ToJSON()),
                    new JProperty("gridConnectionPointId",  GridConnectionPointId),
                    new JProperty("sentBy",                 SentBy));

        #endregion

        #region (static) TryParseStored(JSON, out GridEvent, out Error)

        /// <summary>
        /// A limit, as it is kept.
        /// </summary>
        public static Boolean TryParseStored(JObject                              JSON,
                                             [NotNullWhen(true)]  out GridEvent?  GridEvent,
                                             [NotNullWhen(false)] out String?     Error)
        {

            GridEvent = null;

            if (JSON["event"] is not JObject eventJSON)
            {
                Error = "A limit is kept with its 'event'.";
                return false;
            }

            if (!Event.TryParse(eventJSON, out var parsed, out Error))
                return false;

            if (!parsed.Id.HasValue)
            {
                Error = "A limit is kept with the identification of its event.";
                return false;
            }

            GridEvent = new GridEvent(parsed,
                                      JSON.Value<String>("gridConnectionPointId"),
                                      JSON.Value<String>("sentBy") ?? "");

            return true;

        }

        #endregion

    }

}
