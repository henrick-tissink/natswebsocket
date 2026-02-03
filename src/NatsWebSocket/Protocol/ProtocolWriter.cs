using System;
using System.Text;

namespace NatsWebSocket.Protocol
{
    /// <summary>
    /// Serializes NATS protocol commands to byte arrays for transmission.
    /// </summary>
    internal static class ProtocolWriter
    {
        private static readonly byte[] CrLf = Encoding.UTF8.GetBytes("\r\n");

        public static byte[] Connect(string connectJson)
        {
            return Encoding.UTF8.GetBytes("CONNECT " + connectJson + "\r\n");
        }

        private static readonly byte[] PingBytes = Encoding.UTF8.GetBytes("PING\r\n");
        private static readonly byte[] PongBytes = Encoding.UTF8.GetBytes("PONG\r\n");

        public static byte[] Ping() => PingBytes;

        public static byte[] Pong() => PongBytes;

        public static byte[] Sub(string subject, string sid, string queueGroup = null)
        {
            if (queueGroup != null)
                return Encoding.UTF8.GetBytes($"SUB {subject} {queueGroup} {sid}\r\n");
            return Encoding.UTF8.GetBytes($"SUB {subject} {sid}\r\n");
        }

        public static byte[] Unsub(string sid, int maxMessages = 0)
        {
            if (maxMessages > 0)
                return Encoding.UTF8.GetBytes($"UNSUB {sid} {maxMessages}\r\n");
            return Encoding.UTF8.GetBytes($"UNSUB {sid}\r\n");
        }

        /// <summary>
        /// Build a PUB command: PUB subject [reply-to] #bytes\r\n[payload]\r\n
        /// </summary>
        public static byte[] Pub(string subject, string replyTo, byte[] payload)
        {
            string cmdLine;
            if (replyTo != null)
                cmdLine = $"PUB {subject} {replyTo} {payload.Length}\r\n";
            else
                cmdLine = $"PUB {subject} {payload.Length}\r\n";

            var cmd = Encoding.UTF8.GetBytes(cmdLine);
            var result = new byte[cmd.Length + payload.Length + CrLf.Length];
            Buffer.BlockCopy(cmd, 0, result, 0, cmd.Length);
            Buffer.BlockCopy(payload, 0, result, cmd.Length, payload.Length);
            Buffer.BlockCopy(CrLf, 0, result, cmd.Length + payload.Length, CrLf.Length);
            return result;
        }

        /// <summary>
        /// Build an HPUB command: HPUB subject [reply-to] hdr_len total_len\r\n[headers+payload]\r\n
        /// </summary>
        public static byte[] HPub(string subject, string replyTo, byte[] headerBytes, byte[] payload)
        {
            var hdrLen = headerBytes.Length;
            var totalLen = hdrLen + payload.Length;

            string cmdLine;
            if (replyTo != null)
                cmdLine = $"HPUB {subject} {replyTo} {hdrLen} {totalLen}\r\n";
            else
                cmdLine = $"HPUB {subject} {hdrLen} {totalLen}\r\n";

            var cmd = Encoding.UTF8.GetBytes(cmdLine);
            var result = new byte[cmd.Length + totalLen + CrLf.Length];
            Buffer.BlockCopy(cmd, 0, result, 0, cmd.Length);
            Buffer.BlockCopy(headerBytes, 0, result, cmd.Length, hdrLen);
            Buffer.BlockCopy(payload, 0, result, cmd.Length + hdrLen, payload.Length);
            Buffer.BlockCopy(CrLf, 0, result, cmd.Length + totalLen, CrLf.Length);
            return result;
        }
    }
}
