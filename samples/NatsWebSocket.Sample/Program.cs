using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NatsWebSocket;
using NatsWebSocket.Auth;

namespace NatsWebSocket.Sample
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // ── Config ─────────────────────────────────────────────
            var natsUrl = Environment.GetEnvironmentVariable("NATS_URL") ?? "wss://hermes.sava.africa";
            var credsPath = Environment.GetEnvironmentVariable("NATS_CREDS") ?? FindCredsFile();
            var partnerId = Environment.GetEnvironmentVariable("SAVA_PARTNER_ID") ?? "608c31fd-bc15-4e23-a670-df930a7bcb8e";

            if (credsPath == null || !File.Exists(credsPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Credentials file not found. Set NATS_CREDS environment variable.");
                Console.ResetColor();
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine();
            Console.WriteLine("  NatsWebSocket Sample");
            Console.WriteLine("  ════════════════════");
            Console.ResetColor();
            Console.WriteLine();

            // ── Connect ────────────────────────────────────────────
            var conn = new NatsConnection(new NatsConnectionOptions
            {
                Url = natsUrl,
                AuthHandler = new NKeyAuthHandler(credsPath),
                Name = "NatsWebSocket.Sample",
            });

            conn.StatusChanged += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  [Status] {e.Status}");
                Console.ResetColor();
            };

            conn.Error += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  [Error] {e.Exception.Message}");
                Console.ResetColor();
            };

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine($"  Connecting to {natsUrl}...");
            Console.ResetColor();

            try
            {
                await conn.ConnectAsync();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  Connected!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Connection failed: {ex.Message}");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine();

            // ── KYB Status Query ───────────────────────────────────
            try
            {
                // Get the JWT for the token header
                var jwt = new NKeyAuthHandler(credsPath).Jwt;

                var headers = new NatsHeaders();
                headers.Add("token", jwt);

                var subject = $"svc.kyb.{partnerId}.get";
                var payload = Encoding.UTF8.GetBytes($"{{\"entity_id\":\"{partnerId}\"}}");

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  -> {subject}");
                Console.ResetColor();

                var reply = await conn.RequestAsync(subject, payload, headers);

                if (reply.IsNoResponders)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("  503 No Responders");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"  <- {reply.GetString()}");
                    Console.ResetColor();
                }
            }
            catch (NatsRequestTimeoutException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  Request timed out");
                Console.ResetColor();
            }
            catch (NatsNoRespondersException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  No responders for subject");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"  Error: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine();

            // ── Cleanup ────────────────────────────────────────────
            await conn.CloseAsync();
            conn.Dispose();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Disconnected.");
            Console.ResetColor();

            return 0;
        }

        static string FindCredsFile()
        {
            // Search common locations
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "sava.creds"),
                Path.Combine(AppContext.BaseDirectory, "../../../../credentials/sava.creds"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nats", "sava.creds"),
            };

            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full))
                    return full;
            }

            return null;
        }
    }
}
