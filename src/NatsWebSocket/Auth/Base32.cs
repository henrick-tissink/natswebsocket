using System;

namespace NatsWebSocket.Auth
{
    /// <summary>
    /// RFC 4648 Base32 encoder/decoder for NKEY seed processing.
    /// </summary>
    internal static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static byte[] Decode(string input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            input = input.TrimEnd('=').ToUpperInvariant();
            if (input.Length == 0)
                return Array.Empty<byte>();

            var output = new byte[input.Length * 5 / 8];
            var bitBuffer = 0;
            var bitsRemaining = 0;
            var outputIndex = 0;

            foreach (var c in input)
            {
                var val = Alphabet.IndexOf(c);
                if (val < 0)
                    throw new FormatException($"Invalid base32 character: '{c}'");

                bitBuffer = (bitBuffer << 5) | val;
                bitsRemaining += 5;

                if (bitsRemaining >= 8)
                {
                    output[outputIndex++] = (byte)(bitBuffer >> (bitsRemaining - 8));
                    bitsRemaining -= 8;
                    bitBuffer &= (1 << bitsRemaining) - 1;
                }
            }

            if (outputIndex < output.Length)
            {
                var trimmed = new byte[outputIndex];
                Buffer.BlockCopy(output, 0, trimmed, 0, outputIndex);
                return trimmed;
            }

            return output;
        }

        public static string Encode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length == 0)
                return string.Empty;

            var chars = new char[(data.Length * 8 + 4) / 5];
            var bitBuffer = 0;
            var bitsRemaining = 0;
            var charIndex = 0;

            foreach (var b in data)
            {
                bitBuffer = (bitBuffer << 8) | b;
                bitsRemaining += 8;

                while (bitsRemaining >= 5)
                {
                    chars[charIndex++] = Alphabet[(bitBuffer >> (bitsRemaining - 5)) & 0x1F];
                    bitsRemaining -= 5;
                }
            }

            if (bitsRemaining > 0)
            {
                chars[charIndex++] = Alphabet[(bitBuffer << (5 - bitsRemaining)) & 0x1F];
            }

            return new string(chars, 0, charIndex);
        }
    }
}
