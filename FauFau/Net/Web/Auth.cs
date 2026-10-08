using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using FauFau.Util;

using ROSC = System.ReadOnlySpan<char>;
using SC = System.Span<char>;

namespace FauFau.Net.Web
{
    public static class Auth
    {
        public const string SIG_HEADER_NAME       = @"X-Red5-Signature";
        public const string SIG_HEADER_START      = "Red5 ";
        public const string USER_ID_SALT          = @"-red5salt-2239nknn234j290j09rjdj28fh8fnj234k";
        public const string USER_AUTH_SALT        = @"-red5salt-7nc9bsj4j734ughb8r8dhb8938h8by987c4f7h47b";
        public const string CIPHER_SALT           = @"-cipherSalt";
        public const string PASSFILE_SALT         = @"-Pa55fi1E_$4Lt-";

        private const int SHA1_LENGTH = 20;
        private const int MAX_STACK_LENGTH = 512;

        public static SC GenerateUserId(ROSC email)
        {
            byte[] rented = null;
            int length = Encoding.UTF8.GetByteCount(email) + USER_ID_SALT.Length;
            Span<byte> work = length <= MAX_STACK_LENGTH ? stackalloc byte[length] : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

            int written = Encoding.UTF8.GetBytes(email, work);
            LowerAscii(work.Slice(0, written));
            Encoding.UTF8.GetBytes(USER_ID_SALT, work.Slice(written));

            Span<byte> hash = stackalloc byte[SHA1_LENGTH];
            SHA1.HashData(work, hash);
            Return(rented);

            return Convert.ToBase64String(hash).ToCharArray();
        }

        public static SC GenerateSecret(ROSC email, ROSC password, bool v2 = true)
        {
            byte[] rented = null;
            int length = Encoding.UTF8.GetByteCount(email) + 1 + Encoding.UTF8.GetByteCount(password) + USER_AUTH_SALT.Length;
            Span<byte> work = length <= MAX_STACK_LENGTH ? stackalloc byte[length] : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

            int written = Encoding.UTF8.GetBytes(email, work);
            LowerAscii(work.Slice(0, written));
            work[written++] = (byte)'-';
            written += Encoding.UTF8.GetBytes(password, work.Slice(written));
            Encoding.UTF8.GetBytes(USER_AUTH_SALT, work.Slice(written));

            Span<byte> hash = stackalloc byte[SHA1_LENGTH];
            SHA1.HashData(work, hash);
            Return(rented);

            if (v2)
            {
                for (int i = 0; i < 199; i++)
                {
                    SHA1.HashData(hash, hash);
                }
            }

            SC secret = new char[SHA1_LENGTH * 2];
            Hex.TryEncode(hash, secret, false);
            return secret;
        }

        // The token is a plain HMAC-SHA1 of the request string, keyed with the secret
        public static void GenerateToken(ROSC secret, ROSC headerData, SC tokenOut)
        {
            byte[] rented = null;
            int keyLength = Encoding.UTF8.GetByteCount(secret);
            int dataLength = Encoding.UTF8.GetByteCount(headerData);
            int length = keyLength + dataLength;
            Span<byte> work = length <= MAX_STACK_LENGTH ? stackalloc byte[length] : (rented = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

            Span<byte> key = work.Slice(0, keyLength);
            Span<byte> data = work.Slice(keyLength);
            Encoding.UTF8.GetBytes(secret, key);
            Encoding.UTF8.GetBytes(headerData, data);

            Span<byte> hash = stackalloc byte[SHA1_LENGTH];
            HMACSHA1.HashData(key, data, hash);
            Return(rented);

            Hex.TryEncode(hash, tokenOut, false);
        }

        public static bool Sign(ROSC secret, SC header)
        {
            if (header.Length <= 45 || !header.StartsWith(SIG_HEADER_START))
                return false;

            GenerateToken(secret, GetRequest(header), header.Slice(5, 40));
            return true;
        }
        public static bool Verify(ROSC secret, ROSC header)
        {
            if (header.Length <= 45 || !header.StartsWith(SIG_HEADER_START))
                return false;

            SC generated = stackalloc char[40];

            GenerateToken(secret, GetRequest(header), generated);
            return header.Slice(5, 40).SequenceEqual(generated);
        }

        // The client appends a second token after the request, signed with its hardware cookie
        private static ROSC GetRequest(ROSC header)
        {
            ROSC request = header.Slice(46);
            int end = request.IndexOf(' ');
            return end < 0 ? request : request.Slice(0, end);
        }

        private static void LowerAscii(Span<byte> bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] >= 'A' && bytes[i] <= 'Z')
                {
                    bytes[i] += 32;
                }
            }
        }

        private static void Return(byte[] rented)
        {
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}