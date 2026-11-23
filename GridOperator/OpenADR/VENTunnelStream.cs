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

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// The stream of a tunnel of the REST API of OpenADR - a WebSocket of
    /// "openadr3.1-rest" or a channel of "openadr-rest@open.charging.cloud" -
    /// as the VTN serves it: what it carries, and which VEN came over it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The VTN takes a request that came over it as a request of that VEN, as
    /// one with a bearer token of it over HTTPS: the VEN is the one the
    /// connection that let the login in names - see
    /// <c>ConnectionEntry.VEN</c> -, and nothing the request says changes it.
    /// A bearer token in the request is not looked at.
    /// </para>
    /// <para>
    /// Nothing but this grid operator makes one, and only for a connection it
    /// let in: that is what the VTN believes it for.
    /// </para>
    /// <para>
    /// A request of the JSON messages of OpenADR goes to the VTN as one over
    /// such a stream as well - one of its own, which holds that request and
    /// takes its answer -, marked <see cref="ViaMessages"/>: so that it is the
    /// same request to the VTN, and a subscription made by it may be told over
    /// the connection it came over.
    /// </para>
    /// </remarks>
    public sealed class VENTunnelStream(Stream   Inner,
                                        String   VENName,
                                        String   ConnectionId,
                                        String   Login,
                                        String   Transport,
                                        Boolean  ViaMessages   = false) : Stream
    {

        #region Properties

        /// <summary>
        /// The VEN every request over this stream is a request of.
        /// </summary>
        public String   VENName       { get; } = VENName;

        /// <summary>
        /// The connection of the grid operator that let it in.
        /// </summary>
        public String   ConnectionId  { get; } = ConnectionId;

        /// <summary>
        /// The login it signed in as.
        /// </summary>
        public String   Login         { get; } = Login;

        /// <summary>
        /// What it came over: "WebSocket", "WebSocket TLS" or "SSH".
        /// </summary>
        public String   Transport     { get; } = Transport;

        /// <summary>
        /// Whether the request over it is one of the JSON messages of OpenADR,
        /// rather than one of the REST API in a tunnel.
        /// </summary>
        public Boolean  ViaMessages   { get; } = ViaMessages;

        public override Boolean  CanRead   => Inner.CanRead;
        public override Boolean  CanWrite  => Inner.CanWrite;
        public override Boolean  CanSeek   => false;
        public override Int64    Length    => throw new NotSupportedException();
        public override Int64    Position  { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        #endregion


        #region Read / Write

        public override ValueTask<Int32> ReadAsync(Memory<Byte> Buffer, CancellationToken CancellationToken = default)
            => Inner.ReadAsync(Buffer, CancellationToken);

        public override Task<Int32> ReadAsync(Byte[] Buffer, Int32 Offset, Int32 Count, CancellationToken CancellationToken)
            => Inner.ReadAsync(Buffer, Offset, Count, CancellationToken);

        public override Int32 Read(Byte[] Buffer, Int32 Offset, Int32 Count)
            => Inner.Read(Buffer, Offset, Count);

        public override ValueTask WriteAsync(ReadOnlyMemory<Byte> Buffer, CancellationToken CancellationToken = default)
            => Inner.WriteAsync(Buffer, CancellationToken);

        public override Task WriteAsync(Byte[] Buffer, Int32 Offset, Int32 Count, CancellationToken CancellationToken)
            => Inner.WriteAsync(Buffer, Offset, Count, CancellationToken);

        public override void Write(Byte[] Buffer, Int32 Offset, Int32 Count)
            => Inner.Write(Buffer, Offset, Count);

        public override void  Flush() => Inner.Flush();
        public override Task  FlushAsync(CancellationToken CancellationToken) => Inner.FlushAsync(CancellationToken);
        public override Int64 Seek(Int64 Offset, SeekOrigin Origin) => throw new NotSupportedException();
        public override void  SetLength(Int64 Value) => throw new NotSupportedException();

        #endregion

        #region Dispose

        protected override void Dispose(Boolean Disposing)
        {
            if (Disposing)
                Inner.Dispose();
            base.Dispose(Disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await Inner.DisposeAsync();
            await base.DisposeAsync();
        }

        #endregion

    }

}
