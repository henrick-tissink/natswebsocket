using System;

namespace NatsWebSocket.Auth
{
    /// <summary>
    /// User/password authentication handler.
    /// </summary>
    public sealed class UserPasswordAuthHandler : INatsAuthHandler
    {
        private readonly string _user;
        private readonly string _pass;

        public UserPasswordAuthHandler(string user, string pass)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
            _pass = pass ?? throw new ArgumentNullException(nameof(pass));
        }

        public NatsAuthResult Authenticate(string nonce)
        {
            return new NatsAuthResult { User = _user, Pass = _pass };
        }
    }
}
