using System;
using System.Text;

namespace NatsWebSocket.Protocol
{
    /// <summary>
    /// Parsed NATS message from the read loop.
    /// </summary>
    internal sealed class ParsedMsg
    {
        public string Command { get; set; }  // MSG, HMSG, PING, PONG, +OK, -ERR, INFO
        public string Subject { get; set; }
        public string Sid { get; set; }
        public string ReplyTo { get; set; }
        public byte[] HeaderBytes { get; set; }
        public byte[] Payload { get; set; }
        public string RawLine { get; set; }  // For INFO, -ERR, etc.
    }

    /// <summary>
    /// Ring-buffer based NATS protocol parser. Incoming bytes are appended,
    /// and complete messages are consumed with O(1) pointer advances.
    /// Compacts only when write position nears buffer end.
    /// </summary>
    internal sealed class ProtocolReader
    {
        private byte[] _buffer;
        private int _readPos;
        private int _writePos;

        // Pre-computed bytes for fast-path detection
        private static readonly byte P = (byte)'P';
        private static readonly byte I = (byte)'I';
        private static readonly byte N = (byte)'N';
        private static readonly byte G = (byte)'G';
        private static readonly byte O = (byte)'O';
        private static readonly byte CR = (byte)'\r';
        private static readonly byte LF = (byte)'\n';

        public ProtocolReader(int initialCapacity = 65536)
        {
            _buffer = new byte[initialCapacity];
            _readPos = 0;
            _writePos = 0;
        }

        /// <summary>
        /// Number of unconsumed bytes in the buffer.
        /// </summary>
        public int Available => _writePos - _readPos;

        /// <summary>
        /// Append incoming bytes to the buffer.
        /// </summary>
        public void Append(byte[] data, int offset, int count)
        {
            EnsureCapacity(count);
            Buffer.BlockCopy(data, offset, _buffer, _writePos, count);
            _writePos += count;
        }

        /// <summary>
        /// Try to parse the next complete NATS message from the buffer.
        /// Returns null if more data is needed.
        /// </summary>
        public ParsedMsg TryParse()
        {
            while (true)
            {
                // Fast-path: check for PING\r\n or PONG\r\n by raw bytes before string decode
                var avail = _writePos - _readPos;
                if (avail >= 6)
                {
                    if (_buffer[_readPos] == P)
                    {
                        if (_buffer[_readPos + 1] == I &&
                            _buffer[_readPos + 2] == N &&
                            _buffer[_readPos + 3] == G &&
                            _buffer[_readPos + 4] == CR &&
                            _buffer[_readPos + 5] == LF)
                        {
                            _readPos += 6;
                            return new ParsedMsg { Command = "PING" };
                        }
                        if (_buffer[_readPos + 1] == O &&
                            _buffer[_readPos + 2] == N &&
                            _buffer[_readPos + 3] == G &&
                            _buffer[_readPos + 4] == CR &&
                            _buffer[_readPos + 5] == LF)
                        {
                            _readPos += 6;
                            return new ParsedMsg { Command = "PONG" };
                        }
                    }
                }

                var crlfPos = FindCrlf(_readPos);
                if (crlfPos < 0)
                    return null;

                var lineLength = crlfPos - _readPos;
                var line = Encoding.UTF8.GetString(_buffer, _readPos, lineLength);

                if (line == "PING")
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "PING" };
                }
                if (line == "PONG")
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "PONG" };
                }
                if (line.StartsWith("+OK"))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "+OK" };
                }
                if (line.StartsWith("-ERR"))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "-ERR", RawLine = line.Length > 5 ? line.Substring(5).Trim().Trim('\'') : line };
                }
                if (line.StartsWith("INFO "))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "INFO", RawLine = line.Substring(5) };
                }
                if (line.StartsWith("MSG "))
                {
                    var msg = TryConsumeMsg(line, crlfPos);
                    if (msg == null) return null; // need more data
                    return msg;
                }
                if (line.StartsWith("HMSG "))
                {
                    var msg = TryConsumeHmsg(line, crlfPos);
                    if (msg == null) return null; // need more data
                    return msg;
                }

                // Unknown command, skip
                _readPos = crlfPos + 2;
            }
        }

        /// <summary>
        /// Parse MSG: MSG subject sid [reply-to] #bytes\r\n[payload]\r\n
        /// </summary>
        private ParsedMsg TryConsumeMsg(string cmdLine, int crlfPos)
        {
            var parts = cmdLine.Split(' ');
            string subject, sid, replyTo = null;
            int byteCount;

            if (parts.Length == 4)
            {
                subject = parts[1];
                sid = parts[2];
                if (!int.TryParse(parts[3], out byteCount))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "-ERR", RawLine = "Malformed MSG: invalid byte count" };
                }
            }
            else if (parts.Length == 5)
            {
                subject = parts[1];
                sid = parts[2];
                replyTo = parts[3];
                if (!int.TryParse(parts[4], out byteCount))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "-ERR", RawLine = "Malformed MSG: invalid byte count" };
                }
            }
            else
            {
                _readPos = crlfPos + 2;
                return new ParsedMsg { Command = "-ERR", RawLine = "Malformed MSG: unexpected part count" };
            }

            var dataStart = crlfPos + 2;
            var totalNeeded = dataStart + byteCount + 2;
            if (_writePos < totalNeeded) return null;

            var payload = new byte[byteCount];
            Buffer.BlockCopy(_buffer, dataStart, payload, 0, byteCount);
            _readPos = totalNeeded;

            return new ParsedMsg
            {
                Command = "MSG",
                Subject = subject,
                Sid = sid,
                ReplyTo = replyTo,
                Payload = payload
            };
        }

        /// <summary>
        /// Parse HMSG: HMSG subject sid [reply-to] hdr_len total_len\r\n[headers+payload]\r\n
        /// </summary>
        private ParsedMsg TryConsumeHmsg(string cmdLine, int crlfPos)
        {
            var parts = cmdLine.Split(' ');
            string subject, sid, replyTo = null;
            int hdrLen, totalLen;

            if (parts.Length == 5)
            {
                subject = parts[1];
                sid = parts[2];
                if (!int.TryParse(parts[3], out hdrLen) || !int.TryParse(parts[4], out totalLen))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "-ERR", RawLine = "Malformed HMSG: invalid lengths" };
                }
            }
            else if (parts.Length == 6)
            {
                subject = parts[1];
                sid = parts[2];
                replyTo = parts[3];
                if (!int.TryParse(parts[4], out hdrLen) || !int.TryParse(parts[5], out totalLen))
                {
                    _readPos = crlfPos + 2;
                    return new ParsedMsg { Command = "-ERR", RawLine = "Malformed HMSG: invalid lengths" };
                }
            }
            else
            {
                _readPos = crlfPos + 2;
                return new ParsedMsg { Command = "-ERR", RawLine = "Malformed HMSG: unexpected part count" };
            }

            var dataStart = crlfPos + 2;
            var totalNeeded = dataStart + totalLen + 2;
            if (_writePos < totalNeeded) return null;

            var headerBytes = new byte[hdrLen];
            Buffer.BlockCopy(_buffer, dataStart, headerBytes, 0, hdrLen);

            var payloadLen = totalLen - hdrLen;
            var payload = new byte[payloadLen];
            if (payloadLen > 0)
                Buffer.BlockCopy(_buffer, dataStart + hdrLen, payload, 0, payloadLen);

            _readPos = totalNeeded;

            return new ParsedMsg
            {
                Command = "HMSG",
                Subject = subject,
                Sid = sid,
                ReplyTo = replyTo,
                HeaderBytes = headerBytes,
                Payload = payload
            };
        }

        private int FindCrlf(int start)
        {
            for (int i = start; i < _writePos - 1; i++)
            {
                if (_buffer[i] == CR && _buffer[i + 1] == LF)
                    return i;
            }
            return -1;
        }

        private void EnsureCapacity(int additionalBytes)
        {
            var available = _buffer.Length - _writePos;
            if (available >= additionalBytes)
                return;

            // Try compacting first
            if (_readPos > 0)
            {
                var remaining = _writePos - _readPos;
                Buffer.BlockCopy(_buffer, _readPos, _buffer, 0, remaining);
                _writePos = remaining;
                _readPos = 0;

                if (_buffer.Length - _writePos >= additionalBytes)
                    return;
            }

            // Grow buffer
            var newSize = _buffer.Length;
            while (newSize - _writePos < additionalBytes)
                newSize *= 2;

            var newBuffer = new byte[newSize];
            var count = _writePos - _readPos;
            Buffer.BlockCopy(_buffer, _readPos, newBuffer, 0, count);
            _buffer = newBuffer;
            _writePos = count;
            _readPos = 0;
        }
    }
}
