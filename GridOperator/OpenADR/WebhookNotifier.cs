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
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

using Newtonsoft.Json;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// The webhooks of OpenADR 3.1: the callback of an object operation is
    /// checked before the subscription is kept, and is told of what it asked
    /// for by a POST of the notification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Checked as the Definitions of 3.1 ask: a GET of the callback with a
    /// random "echo" parameter, which the VEN answers with 200 and that value
    /// as the body. A callback that does not is no subscription.
    /// </para>
    /// <para>
    /// A callback is HTTPS. Plain HTTP is taken only from a VEN that reached
    /// the VTN over plain HTTP itself, which is a grid operator without TLS -
    /// a test bench, say: a callback in the clear tells nothing that the
    /// requests of that VEN do not already. A VEN that came in over WebSocket
    /// or SSH is always asked for HTTPS.
    /// </para>
    /// <para>
    /// Not done: blocking private addresses and signing the notifications,
    /// both of which the Definitions suggest; a redirect is not followed.
    /// </para>
    /// </remarks>
    public sealed class WebhookNotifier : IDisposable
    {

        #region Data

        /// <summary>
        /// How long the VEN has to answer, the check of a callback and a notification alike.
        /// </summary>
        public static readonly TimeSpan  Timeout = TimeSpan.FromSeconds(15);

        private readonly HttpClient    http;
        private readonly GridOperator  gridOperator;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// The webhooks of the VTN of the given grid operator.
        /// </summary>
        /// <param name="GridOperator">The grid operator, whose log is written to.</param>
        /// <param name="Handler">How to reach the callbacks; null for the network.</param>
        public WebhookNotifier(GridOperator         GridOperator,
                               HttpMessageHandler?  Handler   = null)
        {

            this.gridOperator  = GridOperator;
            this.http          = new HttpClient(Handler ?? new SocketsHttpHandler {
                                                                AllowAutoRedirect  = false,
                                                                ConnectTimeout     = Timeout
                                                            },
                                                disposeHandler: true) {
                                     Timeout = Timeout
                                 };

            http.DefaultRequestHeaders.UserAgent.ParseAdd($"GridOperator/{typeof(WebhookNotifier).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}");

        }

        #endregion


        #region (static) IsAcceptableCallback(CallbackURL, AllowHTTP, out Error)

        /// <summary>
        /// Whether the given callback may be one: an absolute HTTPS URL, or an
        /// HTTP one where that is taken.
        /// </summary>
        /// <param name="CallbackURL">The callback of an object operation.</param>
        /// <param name="AllowHTTP">Whether plain HTTP is taken.</param>
        /// <param name="Error">Why it may not.</param>
        public static Boolean IsAcceptableCallback(String                            CallbackURL,
                                                   Boolean                           AllowHTTP,
                                                   [NotNullWhen(false)] out String?  Error)
        {

            if (!Uri.TryCreate(CallbackURL, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                Error = $"The callback '{CallbackURL}' is no absolute HTTP URL.";
                return false;
            }

            if (uri.Scheme == Uri.UriSchemeHttp && !AllowHTTP)
            {
                Error = $"The callback '{CallbackURL}' is plain HTTP; OpenADR 3.1 asks for HTTPS, and this VTN takes plain HTTP only from a VEN that reaches it over plain HTTP.";
                return false;
            }

            Error = null;
            return true;

        }

        #endregion

        #region VerifyAsync(CallbackURL, BearerToken, CancellationToken)

        /// <summary>
        /// Check the given callback: a GET with a random "echo", answered with
        /// 200 and the echo as the body.
        /// </summary>
        /// <param name="CallbackURL">The callback.</param>
        /// <param name="BearerToken">The bearer token the VEN asked to be called with.</param>
        /// <param name="CancellationToken">A token to cancel the check.</param>
        /// <returns>Null where the callback answered as it should, else why it did not.</returns>
        public async Task<String?> VerifyAsync(String             CallbackURL,
                                               String?            BearerToken,
                                               CancellationToken  CancellationToken   = default)
        {

            var echo     = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var uri      = new UriBuilder(CallbackURL);
            uri.Query    = (uri.Query.Length > 1 ? uri.Query[1..] + "&" : "") + "echo=" + echo;

            try
            {

                using var request = new HttpRequestMessage(HttpMethod.Get, uri.Uri);

                if (BearerToken is not null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);

                using var response  = await http.SendAsync(request, CancellationToken).ConfigureAwait(false);
                var body            = (await response.Content.ReadAsStringAsync(CancellationToken).ConfigureAwait(false)).Trim();

                if ((Int32) response.StatusCode != 200)
                    return $"The callback '{CallbackURL}' answered the check with {(Int32) response.StatusCode}, not 200.";

                // The value as the body, or as a JSON string.
                if (body != echo && body != $"\"{echo}\"")
                    return $"The callback '{CallbackURL}' did not answer the check with the echo it was sent.";

                return null;

            }
            catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                return $"The callback '{CallbackURL}' could not be checked: {e.GetBaseException().Message}";
            }

        }

        #endregion

        #region Deliver(Notification)

        /// <summary>
        /// Tell the callback of the given notification of it, in the
        /// background: a VEN that does not answer holds up nothing.
        /// </summary>
        /// <param name="Notification">The notification.</param>
        public void Deliver(OpenADRNotification Notification)

            => _ = Task.Run(() => DeliverAsync(Notification));


        /// <summary>
        /// Tell the callback of the given notification of it.
        /// </summary>
        /// <param name="Notification">The notification.</param>
        /// <param name="CancellationToken">A token to cancel it.</param>
        /// <returns>Whether the VEN took it with a 2xx.</returns>
        public async Task<Boolean> DeliverAsync(OpenADRNotification  Notification,
                                                CancellationToken    CancellationToken   = default)
        {

            var callback = Notification.ObjectOperation.CallbackURL.ToString();

            try
            {

                using var request = new HttpRequestMessage(HttpMethod.Post, callback) {
                                        Content = new StringContent(Notification.Notification.ToJSON().ToString(Formatting.None),
                                                                    Encoding.UTF8,
                                                                    "application/json")
                                    };

                if (Notification.ObjectOperation.BearerToken is String bearer)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

                using var response = await http.SendAsync(request, CancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    gridOperator.Log.Warning($"The webhook '{callback}' of '{Notification.Subscription.ClientID}' answered the notification {Notification.Notification.Operation} {Notification.Notification.ObjectType} with {(Int32) response.StatusCode}.",
                                             "openadr", "subscription");
                    return false;
                }

                return true;

            }
            catch (Exception e) when (e is not OperationCanceledException || !CancellationToken.IsCancellationRequested)
            {
                gridOperator.Log.Warning($"The webhook '{callback}' of '{Notification.Subscription.ClientID}' could not be told of {Notification.Notification.Operation} {Notification.Notification.ObjectType}: {e.GetBaseException().Message}",
                                         "openadr", "subscription");
                return false;
            }

        }

        #endregion


        #region Dispose()

        /// <summary>
        /// Let go of the HTTP client.
        /// </summary>
        public void Dispose()
            => http.Dispose();

        #endregion

    }

}
