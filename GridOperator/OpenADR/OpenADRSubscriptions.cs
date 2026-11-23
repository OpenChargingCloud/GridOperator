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

using cloud.charging.open.protocols.OpenADRv3;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// One notification of OpenADR 3.1 that is to be delivered: to whom, by
    /// which of the object operations of the subscription, and what.
    /// </summary>
    /// <remarks>
    /// How it is delivered is not decided here. The webhook of the object
    /// operation is one way - see <see cref="WebhookNotifier"/>; a message over
    /// the connection the subscription came in on is another.
    /// </remarks>
    /// <param name="Subscription">The subscription it is for.</param>
    /// <param name="ObjectOperation">The object operation of the subscription that matched.</param>
    /// <param name="Notification">The notification: the object as the subscriber may see it, and what was done to it.</param>
    public sealed record OpenADRNotification(Subscription     Subscription,
                                             ObjectOperation  ObjectOperation,
                                             Notification     Notification);


    /// <summary>
    /// The subscriptions of the VENs at the VTN of this grid operator, and
    /// which of them a change is to be told to, as OpenADR 3.1 has it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept in memory, as the tokens and the reports are: a VEN subscribes
    /// again after a restart of the grid operator, as it signs in again.
    /// </para>
    /// <para>
    /// A subscription is the VEN's that made it, by its clientID: only that
    /// VEN reads, changes or removes it. A change to an object a VEN owns -
    /// its report, its subscription - is told only to the subscriptions of
    /// that VEN. A change to a program or event with targets is told only to
    /// the subscriptions of the VENs that may read it, with only the targets
    /// they may see - the object privacy of 3.1, for notifications.
    /// </para>
    /// </remarks>
    public sealed class OpenADRSubscriptions
    {

        #region Data

        /// <summary>
        /// The most subscriptions one VEN keeps.
        /// </summary>
        public const Int32 MaxPerClient = 20;

        private readonly Lock                                subscriptionsLock  = new ();
        private readonly Dictionary<String, Subscription>    subscriptions      = new (StringComparer.Ordinal);
        private readonly Dictionary<String, String>          overConnections    = new (StringComparer.Ordinal);

        #endregion


        #region (static) IsConnectionCallback(CallbackURL)

        /// <summary>
        /// Whether the given callback is a connection of the JSON messages of
        /// OpenADR - ws://, wss:// or ssh:// - rather than a webhook: what a VEN
        /// says that subscribes over such a connection, to be told over it.
        /// </summary>
        /// <param name="CallbackURL">The callback of an object operation.</param>
        public static Boolean IsConnectionCallback(String CallbackURL)

            => Uri.TryCreate(CallbackURL, UriKind.Absolute, out var uri) &&
               (uri.Scheme == "ws" || uri.Scheme == "wss" || uri.Scheme == "ssh");

        #endregion

        #region DeliverOverConnection(Id, ConnectionId) / ConnectionOf(Id)

        /// <summary>
        /// Tell what the given subscription asks to be told over the given
        /// connection of the JSON messages of OpenADR - the one it was made
        /// over -, or over none, for null.
        /// </summary>
        /// <param name="Id">The identification of the subscription.</param>
        /// <param name="ConnectionId">The connection - of the grid operator, which a VEN that connects again comes in under again -, or null.</param>
        public void DeliverOverConnection(String   Id,
                                          String?  ConnectionId)
        {
            lock (subscriptionsLock)
            {

                if (ConnectionId is null || !subscriptions.ContainsKey(Id))
                    overConnections.Remove(Id);

                else
                    overConnections[Id] = ConnectionId;

            }
        }

        /// <summary>
        /// The connection of the JSON messages of OpenADR the given subscription
        /// is told over, or null.
        /// </summary>
        /// <param name="Id">The identification of the subscription.</param>
        public String? ConnectionOf(String Id)
        {
            lock (subscriptionsLock)
                return overConnections.TryGetValue(Id, out var connectionId)
                           ? connectionId
                           : null;
        }

        #endregion


        #region All() / Of(ClientID) / Get(Id, ClientID)

        /// <summary>
        /// Every subscription, as the grid operator sees them.
        /// </summary>
        public IReadOnlyList<Subscription> All()
        {
            lock (subscriptionsLock)
                return [ .. subscriptions.Values ];
        }

        /// <summary>
        /// The subscriptions of the given client, oldest first.
        /// </summary>
        /// <param name="ClientID">The clientID of a VEN.</param>
        public IReadOnlyList<Subscription> Of(String ClientID)
        {
            lock (subscriptionsLock)
                return [ .. subscriptions.Values.
                                          Where  (subscription => subscription.ClientID == ClientID).
                                          OrderBy(subscription => subscription.Created) ];
        }

        /// <summary>
        /// The subscription of the given identification, if the given client made it.
        /// </summary>
        /// <param name="Id">The identification of the subscription.</param>
        /// <param name="ClientID">The clientID of who asks.</param>
        public Subscription? Get(String  Id,
                                 String  ClientID)
        {
            lock (subscriptionsLock)
                return subscriptions.TryGetValue(Id, out var subscription) &&
                       subscription.ClientID == ClientID
                           ? subscription
                           : null;
        }

        #endregion

        #region TryAdd(Subscription, out Error) / TryReplace(Subscription) / TryRemove(Id, ClientID)

        /// <summary>
        /// Keep a new subscription, with its identification and its clientID.
        /// </summary>
        /// <param name="Subscription">The subscription.</param>
        /// <param name="Error">Why it is not kept.</param>
        public Boolean TryAdd(Subscription                      Subscription,
                              [NotNullWhen(false)] out String?  Error)
        {

            if (Subscription.Id is not Subscription_Id id || Subscription.ClientID is null)
            {
                Error = "A subscription is kept with its identification and its clientID.";
                return false;
            }

            lock (subscriptionsLock)
            {

                if (subscriptions.Values.Count(subscription => subscription.ClientID == Subscription.ClientID) >= MaxPerClient)
                {
                    Error = $"'{Subscription.ClientID}' has {MaxPerClient} subscriptions already, the most kept for one VEN.";
                    return false;
                }

                subscriptions[id.ToString()] = Subscription;

            }

            Error = null;
            return true;

        }

        /// <summary>
        /// Put the given subscription where the one of the same identification
        /// is, if the same client made that one.
        /// </summary>
        /// <param name="Subscription">The changed subscription.</param>
        /// <returns>The one it replaced, or null where there was none of that client.</returns>
        public Subscription? TryReplace(Subscription Subscription)
        {

            var id = Subscription.Id?.ToString() ?? "";

            lock (subscriptionsLock)
            {

                if (!subscriptions.TryGetValue(id, out var existing) ||
                    existing.ClientID != Subscription.ClientID)
                {
                    return null;
                }

                subscriptions[id] = Subscription;
                return existing;

            }

        }

        /// <summary>
        /// Forget the subscription of the given identification, if the given
        /// client made it.
        /// </summary>
        /// <param name="Id">The identification of the subscription.</param>
        /// <param name="ClientID">The clientID of who asks.</param>
        /// <returns>The one forgotten, or null where there was none of that client.</returns>
        public Subscription? TryRemove(String  Id,
                                       String  ClientID)
        {
            lock (subscriptionsLock)
            {

                if (!subscriptions.TryGetValue(Id, out var existing) ||
                    existing.ClientID != ClientID)
                {
                    return null;
                }

                subscriptions.  Remove(Id);
                overConnections.Remove(Id);
                return existing;

            }
        }

        /// <summary>
        /// Forget every subscription of the given client: a VEN that is no
        /// more is told nothing more.
        /// </summary>
        /// <param name="ClientID">The clientID of the VEN.</param>
        /// <returns>How many were forgotten.</returns>
        public Int32 RemoveAllOf(String ClientID)
        {
            lock (subscriptionsLock)
            {

                var ids = subscriptions.Where (pair => pair.Value.ClientID == ClientID).
                                        Select(pair => pair.Key).
                                        ToArray();

                foreach (var id in ids)
                {
                    subscriptions.  Remove(id);
                    overConnections.Remove(id);
                }

                return ids.Length;

            }
        }

        #endregion


        #region OfOwnObject(ObjectType, Operation, Object, OwnerClientID, ProgramId = null)

        /// <summary>
        /// Who is to be told of a change to an object a VEN owns - a report,
        /// a subscription: the subscriptions of that VEN, no other.
        /// </summary>
        /// <param name="ObjectType">What kind of object it is.</param>
        /// <param name="Operation">What was done to it.</param>
        /// <param name="Object">The object.</param>
        /// <param name="OwnerClientID">The clientID of the VEN that owns it.</param>
        /// <param name="ProgramId">The program it is of, where it is of one.</param>
        public IReadOnlyList<OpenADRNotification> OfOwnObject(ObjectType      ObjectType,
                                                              Operation       Operation,
                                                              IOpenADRObject  Object,
                                                              String          OwnerClientID,
                                                              Program_Id?     ProgramId   = null)

            => [ .. Matching(ObjectType, Operation, ProgramId).
                        Where (match => match.Subscription.ClientID == OwnerClientID).
                        Select(match => new OpenADRNotification(match.Subscription,
                                                                match.ObjectOperation,
                                                                new Notification(ObjectType, Operation, Object))) ];

        #endregion

        #region OfTargetedObject(ObjectType, Operation, ObjectTargets, AsSeenWith, GrantedTargetsOf, ProgramId = null)

        /// <summary>
        /// Who is to be told of a change to a program or an event: the
        /// subscriptions of the VENs that may read it, each with the object
        /// and the targets as that VEN may see them.
        /// </summary>
        /// <param name="ObjectType">What kind of object it is.</param>
        /// <param name="Operation">What was done to it.</param>
        /// <param name="ObjectTargets">The targets of the object; none for one every VEN reads.</param>
        /// <param name="AsSeenWith">The object as a VEN that reads with the given targets sees it.</param>
        /// <param name="GrantedTargetsOf">The targets granted to the VEN of the given clientID; null for none such VEN.</param>
        /// <param name="ProgramId">The program it is of.</param>
        public IReadOnlyList<OpenADRNotification> OfTargetedObject(ObjectType                                     ObjectType,
                                                                   Operation                                      Operation,
                                                                   IReadOnlyCollection<String>                    ObjectTargets,
                                                                   Func<IReadOnlyCollection<String>, IOpenADRObject>  AsSeenWith,
                                                                   Func<String, IReadOnlyCollection<String>?>     GrantedTargetsOf,
                                                                   Program_Id?                                    ProgramId   = null)
        {

            var notifications = new List<OpenADRNotification>();

            foreach (var (subscription, objectOperation) in Matching(ObjectType, Operation, ProgramId))
            {

                if (subscription.ClientID is null ||
                    GrantedTargetsOf(subscription.ClientID) is not IReadOnlyCollection<String> granted)
                {
                    continue;
                }

                // The targets of a subscription filter what it is told of, as
                // the targets of a request filter what is read: met with
                // those granted, never beyond them.
                IReadOnlyCollection<String> readWith = subscription.Targets.Any()
                                                           ? [ .. granted.Intersect(subscription.Targets, StringComparer.Ordinal) ]
                                                           : granted;

                if (ObjectTargets.Count > 0 &&
                   !ObjectTargets.Intersect(readWith, StringComparer.Ordinal).Any())
                {
                    continue;
                }

                notifications.Add(new OpenADRNotification(subscription,
                                                          objectOperation,
                                                          new Notification(ObjectType,
                                                                           Operation,
                                                                           AsSeenWith(readWith),
                                                                           ObjectTargets.Intersect(readWith, StringComparer.Ordinal))));

            }

            return notifications;

        }

        #endregion


        #region (private) Matching(ObjectType, Operation, ProgramId)

        /// <summary>
        /// The subscriptions, and their object operations, that ask for the
        /// given operation on the given kind of object of the given program.
        /// </summary>
        private IReadOnlyList<(Subscription Subscription, ObjectOperation ObjectOperation)> Matching(ObjectType   ObjectType,
                                                                                                    Operation    Operation,
                                                                                                    Program_Id?  ProgramId)
        {

            Subscription[] all;

            lock (subscriptionsLock)
                all = [ .. subscriptions.Values ];

            return [ .. from    subscription    in all
                        where   !subscription.ProgramId.HasValue || subscription.ProgramId == ProgramId
                        from    objectOperation in subscription.ObjectOperations
                        where   objectOperation.Objects.   Contains(ObjectType) &&
                                objectOperation.Operations.Contains(Operation)
                        select  (subscription, objectOperation) ];

        }

        #endregion

    }

}
