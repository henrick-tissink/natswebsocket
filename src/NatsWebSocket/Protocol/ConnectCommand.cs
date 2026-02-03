using System.Collections.Generic;

namespace NatsWebSocket.Protocol
{
    /// <summary>
    /// Builds the CONNECT command JSON payload.
    /// </summary>
    internal static class ConnectCommand
    {
        public static string Build(
            string name,
            bool verbose,
            bool pedantic,
            bool headers,
            bool noResponders,
            string jwt = null,
            string signature = null,
            string authToken = null,
            string user = null,
            string pass = null,
            string nkey = null)
        {
            var fields = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("verbose", verbose),
                new KeyValuePair<string, object>("pedantic", pedantic),
                new KeyValuePair<string, object>("lang", "csharp"),
                new KeyValuePair<string, object>("version", "1.0.0"),
                new KeyValuePair<string, object>("protocol", 1),
                new KeyValuePair<string, object>("headers", headers),
                new KeyValuePair<string, object>("no_responders", noResponders),
                new KeyValuePair<string, object>("name", name),
            };

            if (jwt != null)
                fields.Add(new KeyValuePair<string, object>("jwt", jwt));
            if (signature != null)
                fields.Add(new KeyValuePair<string, object>("sig", signature));
            if (authToken != null)
                fields.Add(new KeyValuePair<string, object>("auth_token", authToken));
            if (user != null)
                fields.Add(new KeyValuePair<string, object>("user", user));
            if (pass != null)
                fields.Add(new KeyValuePair<string, object>("pass", pass));
            if (nkey != null)
                fields.Add(new KeyValuePair<string, object>("nkey", nkey));

            return JsonWriter.WriteObject(fields);
        }
    }
}
