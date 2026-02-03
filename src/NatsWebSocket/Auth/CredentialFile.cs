using System;
using System.IO;

namespace NatsWebSocket.Auth
{
    /// <summary>
    /// Parses NATS .creds files to extract JWT and NKEY seed.
    ///
    /// Format:
    /// -----BEGIN NATS USER JWT-----
    /// eyJ0eXAi...
    /// ------END NATS USER JWT------
    ///
    /// -----BEGIN USER NKEY SEED-----
    /// SUAM...
    /// ------END USER NKEY SEED------
    /// </summary>
    public static class CredentialFile
    {
        public static string ExtractJwt(string credsPath)
        {
            var lines = File.ReadAllLines(credsPath);
            var capture = false;

            foreach (var line in lines)
            {
                if (line.Contains("BEGIN NATS USER JWT"))
                {
                    capture = true;
                    continue;
                }
                if (line.Contains("END NATS USER JWT"))
                    break;
                if (capture && !string.IsNullOrWhiteSpace(line))
                    return line.Trim();
            }

            throw new NatsAuthException("Could not extract JWT from credentials file: " + credsPath);
        }

        public static string ExtractJwtFromText(string credsContent)
        {
            var lines = credsContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var capture = false;

            foreach (var line in lines)
            {
                if (line.Contains("BEGIN NATS USER JWT"))
                {
                    capture = true;
                    continue;
                }
                if (line.Contains("END NATS USER JWT"))
                    break;
                if (capture && !string.IsNullOrWhiteSpace(line))
                    return line.Trim();
            }

            throw new NatsAuthException("Could not extract JWT from credentials content");
        }

        /// <summary>
        /// Extract the raw 32-byte Ed25519 seed from a .creds file.
        /// The NKEY seed line is Base32-encoded with a 2-byte prefix and 2-byte CRC suffix.
        /// </summary>
        public static byte[] ExtractSeed(string credsPath)
        {
            var lines = File.ReadAllLines(credsPath);
            return ExtractSeedFromLines(lines, credsPath);
        }

        public static byte[] ExtractSeedFromText(string credsContent)
        {
            var lines = credsContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return ExtractSeedFromLines(lines, "(inline content)");
        }

        private static byte[] ExtractSeedFromLines(string[] lines, string source)
        {
            var capture = false;
            string seedLine = null;

            foreach (var line in lines)
            {
                if (line.Contains("BEGIN USER NKEY SEED"))
                {
                    capture = true;
                    continue;
                }
                if (line.Contains("END USER NKEY SEED"))
                    break;
                if (capture && !string.IsNullOrWhiteSpace(line))
                {
                    seedLine = line.Trim();
                    break;
                }
            }

            if (string.IsNullOrEmpty(seedLine))
                throw new NatsAuthException("Could not find NKEY seed in credentials: " + source);

            var raw = Base32.Decode(seedLine);

            // Layout: [prefix_byte, type_byte, ...32 seed bytes..., crc_lo, crc_hi]
            if (raw.Length < 36)
                throw new NatsAuthException($"Invalid NKEY seed length after decode: {raw.Length} (expected >= 36)");

            // Validate CRC-16
            var expectedCrc = (ushort)(raw[raw.Length - 2] | (raw[raw.Length - 1] << 8));
            var actualCrc = Crc16(raw, 0, raw.Length - 2);
            if (expectedCrc != actualCrc)
                throw new NatsAuthException(
                    $"NKEY seed CRC validation failed (expected 0x{expectedCrc:X4}, got 0x{actualCrc:X4}) — credentials file may be corrupted");

            var seed = new byte[32];
            Buffer.BlockCopy(raw, 2, seed, 0, 32);
            return seed;
        }

        /// <summary>
        /// CRC-16/CCITT as used by NATS NKEY encoding.
        /// </summary>
        internal static ushort Crc16(byte[] data, int offset, int length)
        {
            ushort crc = 0;
            for (int i = offset; i < offset + length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x1021);
                    else
                        crc = (ushort)(crc << 1);
                }
            }
            return crc;
        }
    }
}
