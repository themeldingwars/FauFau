using System;
using System.Buffers.Text;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using FauFau.Util;
using Microsoft.Extensions.ObjectPool;

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

        private const ulong X36 = 0x3636363636363636;
        private const ulong X5C = 0x5C5C5C5C5C5C5C5C;

        private static ObjectPool<Token> pool = new DefaultObjectPool<Token>(new DefaultPooledObjectPolicy<Token>());


        public static SC GenerateUserId(ROSC email)
        {
            Token token = pool.Get();
            Span<byte> buffer = token.Buffer;
            Span<byte> hash = buffer.Slice(0, 20);
            Span<byte> work = buffer.Slice(20, email.Length + USER_ID_SALT.Length);
            
            Encoding.UTF8.GetBytes(email,  work);
            Encoding.UTF8.GetBytes(USER_ID_SALT, work.Slice(email.Length));
            
            for (int i = 0; i < email.Length; i++)
            {
                if (work[i] > 64 && work[i] < 91)
                {
                    work[i] += 32;
                }
            }
            
            token.SHA1.TryComputeHash(work, hash, out _);
            Base64.EncodeToUtf8(hash, work, out _, out _);
            
            SC uid = new char[28];
            Encoding.UTF8.GetChars(work.Slice(0, 28), uid);
            
            pool.Return(token);
            return uid;
        }
        
        public static SC GenerateSecret(ROSC email, ROSC password, bool v2 = true)
        {
            Token token = pool.Get();
            Span<byte> buffer = token.Buffer;
            Span<byte> hash = buffer.Slice(0, 20);
            Span<byte> work = buffer.Slice(20);
            
            Encoding.UTF8.GetBytes(email,  work);

            for (int i = 0; i < email.Length; i++)
            {
                if (work[i] > 64 && work[i] < 91)
                {
                    work[i] += 32;
                }
            }
            
            work[email.Length] = 0x2D; // -
            work = work.Slice(email.Length + 1);
            
            Encoding.UTF8.GetBytes(password,  work);
            work = work.Slice(password.Length);
            
            Encoding.UTF8.GetBytes(USER_AUTH_SALT, work);
            
            token.SHA1.TryComputeHash(buffer.Slice(20, email.Length + password.Length + USER_AUTH_SALT.Length + 1), hash, out _);

            if (v2)
            {
                for (int i = 0; i < 199; i++)
                {
                    token.SHA1.TryComputeHash(hash, hash, out _);
                }
            }
            
            SC secret = new char[40];
            Common.TryWriteBytesAsHex(hash, secret, false);
            pool.Return(token);
            return secret;
        }
        public static void GenerateToken(ROSC secret, ROSC headerData, SC tokenOut)
        {
            Token token = pool.Get();

            Span<byte> buffer = token.Buffer;
            Span<byte> left = buffer.Slice(0, 64);
            Span<byte> right = buffer.Slice(64);
            Span<ulong> xor = MemoryMarshal.Cast<byte, ulong>(buffer.Slice(0, 64));

            Encoding.UTF8.GetBytes(secret, left);
            
            //for (int i = 0; i < 8; i++) { xor[i] ^= X36; }
            xor[0] ^= X36;
            xor[1] ^= X36;
            xor[2] ^= X36;
            xor[3] ^= X36;
            xor[4] ^= X36;
            xor[5] ^= X36;
            xor[6] ^= X36;
            xor[7] ^= X36;

            Encoding.UTF8.GetBytes(headerData, right);

            token.SHA1.TryComputeHash(buffer.Slice(0, 64 + headerData.Length), right, out _);
            
            //left.Slice(40).Fill(0);
            xor[5] = 0;
            xor[6] = 0;
            xor[7] = 0;
            
            Encoding.UTF8.GetBytes(secret, left);
            
            //for (int i = 0; i < 8; i++) { xor[i] ^= X5C; }
            xor[0] ^= X5C;
            xor[1] ^= X5C;
            xor[2] ^= X5C;
            xor[3] ^= X5C;
            xor[4] ^= X5C;
            xor[5] ^= X5C;
            xor[6] ^= X5C;
            xor[7] ^= X5C;
            
            token.SHA1.TryComputeHash(buffer.Slice(0, 84), right, out _);
            Common.TryWriteBytesAsHex(right.Slice(0, 20), tokenOut, false);
            
            pool.Return(token);
        }
        public static bool Sign(ROSC secret, SC header)
        {
            if (header.Length <= 45 || !header.StartsWith(SIG_HEADER_START))
                return false;

            GenerateToken(secret, header.Slice(46), header.Slice(5, 40));;
            return true;
        }
        public static bool Verify(ROSC secret, ROSC header)
        {
            if (header.Length <= 45 || !header.StartsWith(SIG_HEADER_START))
                return false;

            SC generated = stackalloc char[40];
            
            GenerateToken(secret, header.Slice(46), generated);
            return header.Slice(5, 40).SequenceEqual(generated);
        }
        
        private class Token
        {
            public SHA1 SHA1 = SHA1.Create();
            public byte[] Buffer = new byte[1024];
        }
    }
}