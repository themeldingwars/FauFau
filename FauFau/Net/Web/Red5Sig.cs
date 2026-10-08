using System;
using System.Security.Cryptography;
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
            public ulong              Cid;
            public int                Version;
        }

        // Read the header and return it broken up into its parts
        public static QsValues ParseString(ReadOnlySpan<char> headerStr)
        {
            var sig      = new QsValues();
            var red5Text = ReadUntil(ref headerStr, ' ');

            if (!red5Text.Equals("red5", StringComparison.InvariantCultureIgnoreCase))
                return sig;

            sig.Token = ReadUntil(ref headerStr, ' ');
            var qs = ReadUntil(ref headerStr, ' ');
            sig.Token2 = headerStr;

            do {
                var kvpStr = ReadUntil(ref qs, '&');
                if (kvpStr.Length == 0)
                    break;

                var split = kvpStr.IndexOf('=');
                var key   = split < 0 ? kvpStr : kvpStr.Slice(0, split);
                var value = split < 0 ? ReadOnlySpan<char>.Empty : kvpStr.Slice(split + 1);

                if (key.Equals("ver", StringComparison.InvariantCultureIgnoreCase))
                    sig.Version = int.TryParse(value, out var version) ? version : 0;
                else if (key.Equals("tc", StringComparison.InvariantCultureIgnoreCase))
                    sig.Time = uint.TryParse(value, out var time) ? time : 0;
                else if (key.Equals("nonce", StringComparison.InvariantCultureIgnoreCase))
                    sig.Nonce = value;
                else if (key.Equals("uid", StringComparison.InvariantCultureIgnoreCase))
                    sig.UID = value;
                else if (key.Equals("host", StringComparison.InvariantCultureIgnoreCase))
                    sig.Host = HttpUtility.UrlDecode(value.ToString());
                else if (key.Equals("path", StringComparison.InvariantCultureIgnoreCase))
                    sig.Path = HttpUtility.UrlDecode(value.ToString());
                else if (key.Equals("hbody", StringComparison.InvariantCultureIgnoreCase))
                    sig.Body = value;
                else if (key.Equals("cid", StringComparison.InvariantCultureIgnoreCase))
                    sig.Cid = ulong.TryParse(value, out var cid) ? cid : 0;
            } while (true);

            return sig;
        }

        // Returns everything before the separator and drops it from the source, or the whole source without one
        private static ReadOnlySpan<char> ReadUntil(scoped ref ReadOnlySpan<char> source, char separator)
        {
            var end = source.IndexOf(separator);
            if (end < 0)
            {
                var all = source;
                source = ReadOnlySpan<char>.Empty;
                return all;
            }

            var part = source.Slice(0, end);
            source = source.Slice(end + 1);
            return part;
        }

        public static ReadOnlySpan<char> GenerateUserId(ReadOnlySpan<char> email, bool urlEncode = false)
        {
            var uid = Auth.GenerateUserId(email);
            return urlEncode ? HttpUtility.UrlEncode(uid.ToString()) : uid;
        }

        public static ReadOnlySpan<char> GenerateSecret(ReadOnlySpan<char> email, ReadOnlySpan<char> password)
        {
            return Auth.GenerateSecret(email, password);
        }

        public static ReadOnlySpan<char> CreateRequestString(ReadOnlySpan<char> uid,  ReadOnlySpan<char> host,
                                                             ReadOnlySpan<char> path, ReadOnlySpan<char> hbody,
                                                             ulong              cid = 0)
        {
            var time  = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = Common.BytesToHexString(RandomNumberGenerator.GetBytes(8), false);

            return CreateRequestString(uid, host, path, hbody, cid, time, nonce);
        }

        // uid, host and path use uppercase RFC 3986 escapes, the time is in seconds
        public static ReadOnlySpan<char> CreateRequestString(ReadOnlySpan<char> uid,  ReadOnlySpan<char> host,
                                                             ReadOnlySpan<char> path, ReadOnlySpan<char> hbody,
                                                             ulong              cid,  uint               time,
                                                             ReadOnlySpan<char> nonce)
        {
            var requestStr =
                $"ver={VERSION}&tc={time}&nonce={nonce}&uid={Uri.EscapeDataString(uid.ToString())}&host={Uri.EscapeDataString(host.ToString())}&path={Uri.EscapeDataString(path.ToString())}&hbody={hbody}&cid={cid}";

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