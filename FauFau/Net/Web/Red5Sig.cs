using System;
using System.Web;
using FauFau.Util;

namespace FauFau.Net.Web
{
    // Read and create a red 5 sig that is used in http requests from the client
    public ref struct Red5Sig
    {
        private const int VERSION = 2;

        // Values used in the header
        public ref struct QsValues
        {
            public ReadOnlySpan<char> Token;
            public ReadOnlySpan<char> Token2;
            public uint               Time;
            public ReadOnlySpan<char> Nonce;
            public ReadOnlySpan<char> UID;
            public ReadOnlySpan<char> Path;
            public ReadOnlySpan<char> Host;
            public ReadOnlySpan<char> Body;
            public uint               Cid;
            public int                Version;
        }

        // Read the header and return it broken up into its parts
        public static QsValues ParseString(ReadOnlySpan<char> headerStr)
        {
            var sig          = new QsValues();
            var headerReader = new Spanner<char>(headerStr);
            var red5Text     = headerReader.ReadUntil(' ');

            if (!red5Text.Equals("red5", StringComparison.InvariantCultureIgnoreCase))
                return sig;

            sig.Token = headerReader.ReadUntil(' ');
            var qs = new Spanner<char>(headerReader.ReadUntil(' '));
            sig.Token2 = headerReader.Remaining;

            do {
                var kvpStr = qs.ReadUntil('&');
                if (kvpStr.Length == 0)
                    break;

                var kvp = Spanner<char>.SplitKVP(kvpStr, '=');

                if (kvp.Key.Equals("ver", StringComparison.InvariantCultureIgnoreCase))
                    sig.Version = int.TryParse(kvp.Value, out var version) ? version : 0;
                else if (kvp.Key.Equals("tc", StringComparison.InvariantCultureIgnoreCase))
                    sig.Time = uint.TryParse(kvp.Value, out var time) ? time : 0;
                else if (kvp.Key.Equals("nonce", StringComparison.InvariantCultureIgnoreCase))
                    sig.Nonce = kvp.Value;
                else if (kvp.Key.Equals("uid", StringComparison.InvariantCultureIgnoreCase))
                    sig.UID = kvp.Value;
                else if (kvp.Key.Equals("host", StringComparison.InvariantCultureIgnoreCase))
                    sig.Host = HttpUtility.UrlDecode(kvp.Value.ToString());
                else if (kvp.Key.Equals("path", StringComparison.InvariantCultureIgnoreCase))
                    sig.Path = HttpUtility.UrlDecode(kvp.Value.ToString());
                else if (kvp.Key.Equals("hbody", StringComparison.InvariantCultureIgnoreCase))
                    sig.Body = kvp.Value;
                else if (kvp.Key.Equals("cid", StringComparison.InvariantCultureIgnoreCase))
                    sig.Cid = uint.TryParse(kvp.Value, out var cid) ? cid : 0;
            } while (true);

            return sig;
        }

        public static ReadOnlySpan<char> GenerateUserId(ReadOnlySpan<char> email, bool urlEncode = false)
        {
            var uid = Auth.GenerateUserId(email);
            return urlEncode ? HttpUtility.UrlEncode(uid.ToString()) : uid;
        }

        public static ReadOnlySpan<char> GenerateSecret(ReadOnlySpan<char> email, ReadOnlySpan<char> password)
        {
            return Auth.GenerateSecret(email, password, false);
        }

        public static ReadOnlySpan<char> CreateRequestString(ReadOnlySpan<char> uid,  ReadOnlySpan<char> host,
                                                             ReadOnlySpan<char> path, ReadOnlySpan<char> hbody,
                                                             int                cid = 0)
        {
            var cidStr = Convert.ToString(cid);
            var tc     = DateTimeOffset.UtcNow.ToUnixTimeSeconds() * 1000;
            var nonce  = Common.BytesToHexString(BitConverter.GetBytes(Environment.TickCount), true);
            var requestStr =
                $"ver={VERSION.ToString()}&tc={tc.ToString()}&nonce={nonce.ToString()}&uid={uid.ToString()}&host={host.ToString()}&path={path.ToString()}&hbody={hbody.ToString()}&cid={cidStr}";

            return requestStr.AsSpan();
        }

        public static ReadOnlySpan<char> GenerateToken(ReadOnlySpan<char> secret, ReadOnlySpan<char> reqStr)
        {
            var token = new char[40];
            Auth.GenerateToken(secret, reqStr, token);
            return token;
        }
    }
}