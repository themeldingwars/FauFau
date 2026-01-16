using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using FauFau.Util;

namespace FauFau.Net.Web
{
    // Read and create a red 5 sig that is used in http requests from the client
    public ref struct Red5Sig
    {
        private const int    MAX_STACK_STRING_SIZE = 500;
        private const string SIG_HEADER_NAME       = @"X-Red5-Signature";
        private const string USER_ID_SALT          = @"-red5salt-2239nknn234j290j09rjdj28fh8fnj234k";
        private const string USER_AUTH_SALT        = @"-red5salt-7nc9bsj4j734ughb8r8dhb8938h8by987c4f7h47b";
        private const int    VERSION               = 2;

        private static readonly SHA1 Sha1Hasher = SHA1.Create();

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
            int    length     = email.Length + USER_ID_SALT.Length;
            char[] pooledBuff = null;
            Span<char> preHashed = length > MAX_STACK_STRING_SIZE
                ? (pooledBuff = ArrayPool<char>.Shared.Rent(length))
                : stackalloc char[length];

            email.CopyTo(preHashed);
            USER_ID_SALT.AsSpan().CopyTo(preHashed.Slice(email.Length));

            var strChars = Encoding.UTF8.GetBytes(preHashed.ToArray());
            var hash     = Sha1Hasher.ComputeHash(strChars);
            var b64Hash  = Convert.ToBase64String(hash);
            var uid      = urlEncode ? HttpUtility.UrlEncode(b64Hash) : b64Hash;

            if (pooledBuff != null) {
                ArrayPool<char>.Shared.Return(pooledBuff);
            }

            return uid;
        }

        public static ReadOnlySpan<char> GenerateSecret(ReadOnlySpan<char> email, ReadOnlySpan<char> password)
        {
            var    length     = email.Length + password.Length + USER_AUTH_SALT.Length + 1;
            char[] pooledBuff = null;
            Span<char> preHashed = length > MAX_STACK_STRING_SIZE
                ? (pooledBuff = ArrayPool<char>.Shared.Rent(length))
                : stackalloc char[length];

            email.CopyTo(preHashed);
            "-".AsSpan().CopyTo(preHashed.Slice(email.Length));
            password.CopyTo(preHashed.Slice(email.Length                                  + 1));
            USER_AUTH_SALT.AsSpan().CopyTo(preHashed.Slice(email.Length + password.Length + 1));

            var strChars = Encoding.UTF8.GetBytes(preHashed.ToArray());
            var hash     = Sha1Hasher.ComputeHash(strChars);
            var secret   = Common.BytesToHexString(hash, false);

            if (pooledBuff != null) {
                ArrayPool<char>.Shared.Return(pooledBuff);
            }

            return secret;
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
            const int  TOKEN_LENGTH     = 0x40;
            const int  SHA1_HASH_LENGTH = 20;
            const int  BUFFER_LENGTH    = 500;
            Span<byte> buffer           = stackalloc byte[BUFFER_LENGTH];
            Span<byte> transformA       = stackalloc byte[TOKEN_LENGTH];
            Span<byte> transformB       = stackalloc byte[TOKEN_LENGTH];
            Span<byte> hash1            = stackalloc byte[SHA1_HASH_LENGTH];
            Span<byte> hash2            = stackalloc byte[SHA1_HASH_LENGTH];

            // xor and pad to length
            for (int i = 0; i < TOKEN_LENGTH; i++) {
                var hasSecret = secret.Length != 0  && i <= secret.Length;
                transformA[i] = (byte) (hasSecret ? secret[i] ^ 0x36 : 0x36);
            }

            var hash1InputLength = transformA.Length + reqStr.Length;
            transformA.CopyTo(buffer);
            var reqStrBytes = MemoryMarshal.Cast<char, byte>(reqStr);
            var strChars = Encoding.UTF8.GetBytes(reqStr.ToArray());
            strChars.AsSpan().CopyTo(buffer.Slice(transformA.Length));

            Sha1Hasher.TryComputeHash(buffer.Slice(0, hash1InputLength), hash1, out int hash1NumWritten);

            // again but with a different xor value
            for (int i = 0; i < TOKEN_LENGTH; i++) {
                var hasSecret = secret.Length != 0 && i <= secret.Length;
                transformB[i] = (byte) (hasSecret ? secret[i] ^ 0x5c : 0x5c);
            }

            var hash2InputLength = transformB.Length + hash1.Length;
            transformB.CopyTo(buffer);
            hash1.CopyTo(buffer.Slice(transformB.Length));

            Sha1Hasher.TryComputeHash(buffer.Slice(0, hash2InputLength), hash2, out int hash2NumWritten);

            var token = Common.BytesToHexString(hash2, false).ToString();
            return token;
        }
    }
}