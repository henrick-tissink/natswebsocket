using System;
using System.Text;

namespace NatsWebSocket.JetStream.Internal
{
    /// <summary>
    /// Base64 URL encoding (RFC 4648) for encoding object names in NATS subjects.
    /// </summary>
    internal static class Base64Url
    {
        /// <summary>
        /// Encodes a string to Base64 URL format.
        /// </summary>
        public static string Encode(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var bytes = Encoding.UTF8.GetBytes(input);
            return Encode(bytes);
        }

        /// <summary>
        /// Encodes bytes to Base64 URL format.
        /// </summary>
        public static string Encode(byte[] input)
        {
            if (input == null || input.Length == 0) return string.Empty;

            var base64 = Convert.ToBase64String(input);

            // Convert to URL-safe format
            return base64
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// Decodes a Base64 URL string.
        /// </summary>
        public static string Decode(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var bytes = DecodeBytes(input);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Decodes a Base64 URL string to bytes.
        /// </summary>
        public static byte[] DecodeBytes(string input)
        {
            if (string.IsNullOrEmpty(input)) return Array.Empty<byte>();

            // Convert from URL-safe format
            var base64 = input
                .Replace('-', '+')
                .Replace('_', '/');

            // Add padding if necessary
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }

            return Convert.FromBase64String(base64);
        }
    }

    /// <summary>
    /// NUID generator for unique object identifiers.
    /// </summary>
    internal static class Nuid
    {
        private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const int Base = 62;
        private const int PreLen = 12;
        private const int SeqLen = 10;
        private const int TotalLen = PreLen + SeqLen;

        private static readonly object Lock = new object();
        private static readonly Random Random = new Random();
        private static string _prefix;
        private static long _seq;
        private static long _inc;

        static Nuid()
        {
            ResetPrefix();
        }

        /// <summary>
        /// Generate a new NUID.
        /// </summary>
        public static string Next()
        {
            lock (Lock)
            {
                _seq += _inc;
                if (_seq >= 839299365868340224L) // 62^10
                {
                    ResetPrefix();
                }

                var chars = new char[TotalLen];

                // Copy prefix
                for (int i = 0; i < PreLen; i++)
                    chars[i] = _prefix[i];

                // Encode sequence
                var seq = _seq;
                for (int i = TotalLen - 1; i >= PreLen; i--)
                {
                    chars[i] = Digits[(int)(seq % Base)];
                    seq /= Base;
                }

                return new string(chars);
            }
        }

        private static void ResetPrefix()
        {
            var chars = new char[PreLen];
            for (int i = 0; i < PreLen; i++)
                chars[i] = Digits[Random.Next(Base)];
            _prefix = new string(chars);

            _seq = Random.Next(int.MaxValue);
            _inc = Random.Next(33, 256);
        }
    }
}
