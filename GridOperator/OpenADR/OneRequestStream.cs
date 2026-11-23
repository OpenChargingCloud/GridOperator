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
using System.Globalization;

#endregion

namespace cloud.charging.open.GridOperator.OpenADR
{

    /// <summary>
    /// A connection of HTTP/1.1 of one request, for an HTTP server to serve: it
    /// reads the request it was made with, and then ends, as a client that
    /// closed its half; and what the server writes - the answer - is kept.
    /// </summary>
    /// <remarks>
    /// How a request of the JSON messages of OpenADR reaches the VTN: as the
    /// request of the REST API it stands for, on a connection of its own, so
    /// that the VTN is asked as over any other.
    /// </remarks>
    public sealed class OneRequestStream(Byte[] Request) : Stream
    {

        #region Data

        private readonly MemoryStream  request   = new (Request, writable: false);
        private readonly MemoryStream  response  = new ();
        private readonly Lock          responseLock = new ();

        #endregion

        #region Properties

        /// <summary>
        /// What was written: the answer of the server.
        /// </summary>
        public Byte[] Response
        {
            get
            {
                lock (responseLock)
                    return response.ToArray();
            }
        }

        public override Boolean  CanRead   => true;
        public override Boolean  CanWrite  => true;
        public override Boolean  CanSeek   => false;
        public override Int64    Length    => throw new NotSupportedException();
        public override Int64    Position  { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        #endregion


        #region Read / Write

        public override Int32 Read(Byte[] Buffer, Int32 Offset, Int32 Count)
            => request.Read(Buffer, Offset, Count);

        public override Task<Int32> ReadAsync(Byte[] Buffer, Int32 Offset, Int32 Count, CancellationToken CancellationToken)
            => Task.FromResult(request.Read(Buffer, Offset, Count));

        public override ValueTask<Int32> ReadAsync(Memory<Byte> Buffer, CancellationToken CancellationToken = default)
            => ValueTask.FromResult(request.Read(Buffer.Span));

        public override void Write(Byte[] Buffer, Int32 Offset, Int32 Count)
        {
            lock (responseLock)
                response.Write(Buffer, Offset, Count);
        }

        public override Task WriteAsync(Byte[] Buffer, Int32 Offset, Int32 Count, CancellationToken CancellationToken)
        {
            Write(Buffer, Offset, Count);
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<Byte> Buffer, CancellationToken CancellationToken = default)
        {
            lock (responseLock)
                response.Write(Buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override void  Flush() { }
        public override Task  FlushAsync(CancellationToken CancellationToken) => Task.CompletedTask;
        public override Int64 Seek(Int64 Offset, SeekOrigin Origin) => throw new NotSupportedException();
        public override void  SetLength(Int64 Value) => throw new NotSupportedException();

        #endregion


        #region (static) TryReadResponse(Bytes, out Status, out Body)

        /// <summary>
        /// The status and the body of the first answer of HTTP/1.1 in the given
        /// bytes: with a Content-Length, chunked, or up to the end.
        /// </summary>
        /// <param name="Bytes">What the server wrote.</param>
        /// <param name="Status">The status of the answer.</param>
        /// <param name="Body">Its body, as UTF-8 text; null for none.</param>
        public static Boolean TryReadResponse(Byte[]       Bytes,
                                              out Int32    Status,
                                              out String?  Body)
        {

            Status  = 0;
            Body    = null;

            var headerEnd = IndexOf(Bytes, "\r\n\r\n"u8, 0);

            if (headerEnd < 0)
                return false;

            var lines = Encoding.ASCII.GetString(Bytes, 0, headerEnd).Split("\r\n");
            var first = lines[0].Split(' ', 3);

            if (first.Length < 2 ||
               !first[0].StartsWith("HTTP/1.", StringComparison.Ordinal) ||
               !Int32.TryParse(first[1], NumberStyles.None, CultureInfo.InvariantCulture, out Status))
            {
                return false;
            }

            Int32?   contentLength  = null;
            var      chunked        = false;

            foreach (var line in lines.Skip(1))
            {

                var colon = line.IndexOf(':');

                if (colon <= 0)
                    continue;

                var name   = line[..colon].Trim();
                var value  = line[(colon + 1)..].Trim();

                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    Int32.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                {
                    contentLength = length;
                }

                else if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                         value.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                {
                    chunked = true;
                }

            }

            var start = headerEnd + 4;

            if (chunked)
            {

                using var body = new MemoryStream();
                var position = start;

                while (true)
                {

                    var lineEnd = IndexOf(Bytes, "\r\n"u8, position);

                    if (lineEnd < 0)
                        return false;

                    var sizeText = Encoding.ASCII.GetString(Bytes, position, lineEnd - position).Split(';')[0].Trim();

                    if (!Int32.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
                        return false;

                    position = lineEnd + 2;

                    if (size == 0)
                        break;

                    if (position + size > Bytes.Length)
                        return false;

                    body.Write(Bytes, position, size);
                    position += size + 2;

                }

                Body = Encoding.UTF8.GetString(body.ToArray());
                return true;

            }

            var available = Bytes.Length - start;

            if (contentLength is Int32 declared)
            {

                if (declared > available)
                    return false;

                available = declared;

            }

            Body = available > 0
                       ? Encoding.UTF8.GetString(Bytes, start, available)
                       : null;

            return true;

        }

        #endregion

        #region (private static) IndexOf(Bytes, Pattern, From)

        private static Int32 IndexOf(Byte[]              Bytes,
                                     ReadOnlySpan<Byte>  Pattern,
                                     Int32               From)
        {

            var found = Bytes.AsSpan(From).IndexOf(Pattern);

            return found < 0
                       ? -1
                       : From + found;

        }

        #endregion

    }

}
