using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FauFau.Net.Web;

namespace Tests
{
    public class Red5SigTests
    {
        public static void Test1()
        {
            string testHeader = @"Red5 669d417a26d056693a63f3c63437be717febb615 ver=1&tc=1464664242&nonce=0000295c00007084&uid=d8wP5gy2K%2BrsJYAi8PKf%2BJq4Bv8%3D&host=DUMPTRUCK&path=&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709";
            string testRequestStr = @"ver=2&tc=1596167883&nonce=41ab72d92674182d&uid=RnBt5FRsgY4ZibwS1WzaE0HL%2B9o%3D&host=indev.themeldingwars.com&path=%2Fclientapi%2Fapi%2Fv2%2Faccounts%2Flogin&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0";
            
            var parsedSig3 = Red5Sig.ParseString("dfgdg gfgfg &fdfds= ffdsfs= ".AsSpan());

            for (int i = 0; i < 2000; i++) {
                var testUid = Red5Sig.GenerateUserId("gdfhdfgdgdg@testmail.comdsdfffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffgdffffffffffffffffff");
            }

            var secert1 = Red5Sig.GenerateSecret("", "");
            
            for (int i = 0; i < 2000; i++) {
                var secert2 = Red5Sig.GenerateSecret("", "");
            }

            var correctToken = "4856caef1689dbaec6acb6e7ccb3390f861b08f1";
            var testToken1 = Red5Sig.GenerateToken("", "");

            Console.WriteLine();
            
            //BenchmarkRunner.Run<Red5SigBenchmark>();
        }
        
        [ShortRunJob]
        public class Red5SigBenchmark
        {
            const string testHeader1 = @"Red5 669d417a26d056693a63f3c63437be717febb615 ver=1&tc=1464664242&nonce=0000295c00007084&uid=d8wP5gy2K%2BrsJYAi8PKf%2BJq4Bv8%3D&host=DUMPTRUCK&path=&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709";
            const string testHeader2 = @"Red5 a9c343fd0dabef5d39a7sg75bf7c23e0bd19c477 ver=2&tc=1499479803&nonce=08367a9b7e102ac7&uid=v5W1FUHSSHL9ZXZ3LYuH0VapzX4%3D&host=clientapi-v01-sna01-prod.firefall.com&path=%2Fapi%2Fv2%2Faccounts%2Flogin&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0 4d43e3eab7b67f7bb7c47e031dc63f5506fec77b";
            const string testHeader3 = @"ixslfcjlbpujzrpjzzlfaxtkdqzqbdogvzeiqnazdtffkvycidcwilvkduysurxhvczvaghphihltpujbmenogptyszlxvdrtqholvowjncmmlujpiznhybjgsnnmtzf tEUob7sLYoKU=CeBZu&& ==Xa0Ab6Mkk9GoLZ8BTVhVeVks0Ii8GNurWJDhboRiLRqGgFwUl5xjEACogCkcKLekdNHSa2IzUylDcG9RTOfhbcWGpMkHBWcSugQpdSCu8Bsde87";
            const string testHeader4 = @"red5 ver=ixslfcjlbpujzrpjzzlfaxtkdqzqbdogvzeiqnazdtffkvycidcwilvkduysurxhvczvaghphihltpujbmenogptyszlxvdrtqholvowjncmmlujpiznhybjgsnnmtzf tEUob7sLYoKU=CeBZu&& ==Xa0Ab6Mkk9GoLZ8BTVhVeVks0Ii8GNurWJDhboRiLRqGgFwUl5xjEACogCkcKLekdNHSa2IzUylDcG9RTOfhbcWGpMkHBWcSugQpdSCu8Bsde87";

            private const string USER_ID_SALT = @"-red5salt-2239nknn234j290j09rjdj28fh8fnj234k";
            private const string USER_AUTH_SALT = @"-red5salt-7nc9bsj4j734ughb8r8dhb8938h8by987c4f7h47b";
            private const string userEmail = @"gdsgsdg@test.com";
            private const string userPassword = @"dgdssdgsdgsgfgdfg";
            private static readonly SHA1 Sha1Hasher = SHA1.Create();

            private static byte[] hexBytes = Encoding.UTF8.GetBytes("fhgfhfghfghfgdfkljdfkgdjkl,sdjfksehfnsdjfnsdkjfhnsdjkfdsfjsdhfgjsdjkgds");

            [Benchmark]
            public Red5Sig.QsValues Version1()
            {
                var result = Red5Sig.ParseString(testHeader1.AsSpan());
                return result;
            }
            
            [Benchmark]
            public Red5Sig.QsValues Version2()
            {
                var result = Red5Sig.ParseString(testHeader2.AsSpan());
                return result;
            }
            
            [Benchmark]
            public Red5Sig.QsValues BadString()
            {
                var result = Red5Sig.ParseString(testHeader3.AsSpan());
                return result;
            }
            
            [Benchmark]
            public Red5Sig.QsValues BadString2()
            {
                var result = Red5Sig.ParseString(testHeader4.AsSpan());
                return result;
            }
            
            [Benchmark]
            public ReadOnlySpan<char> GenerateUserId()
            {
                var result = Red5Sig.GenerateUserId(userEmail);
                return result;
            }
            
            [Benchmark]
            public ReadOnlySpan<char> GenerateUserId2()
            {
                Span<char> preHashed = stackalloc char[userEmail.Length + USER_ID_SALT.Length];
                userEmail.AsSpan().CopyTo(preHashed);
                USER_ID_SALT.AsSpan().CopyTo(preHashed.Slice(userEmail.Length));

                var strAsBytes = MemoryMarshal.Cast<char, byte>(preHashed);
                var hash       = Sha1Hasher.ComputeHash(strAsBytes.ToArray());
                return Convert.ToBase64String(hash);
            }
            
            [Benchmark]
            public ReadOnlySpan<char> GenerateSecret()
            {
                var secert1 = Red5Sig.GenerateSecret(userEmail, userPassword);
                return secert1;
            }
            
            [Benchmark]
            public ReadOnlySpan<char> GenrateSecert2()
            {
                Span<char> preHashed = stackalloc char[userEmail.Length + userPassword.Length + USER_AUTH_SALT.Length + 1];
                userEmail.AsSpan().CopyTo(preHashed);
                "-".AsSpan().CopyTo(preHashed.Slice(userEmail.Length));
                userPassword.AsSpan().CopyTo(preHashed.Slice(userEmail.Length + 1));
                USER_AUTH_SALT.AsSpan().CopyTo(preHashed.Slice(userEmail.Length + userPassword.Length + 1));
            
                var strChars = Encoding.UTF8.GetBytes(preHashed.ToArray());
                var hash     = Sha1Hasher.ComputeHash(strChars);
                var secret   = BitConverter.ToString(hash, 0, hash.Length).Replace("-", "");
                return secret;
            }
            
            [Benchmark]
            public ReadOnlySpan<char> ToHexest()
            {
                int          length         = hexBytes.Length * 2;
                var          buffer         = ArrayPool<char>.Shared.Rent(length);
                const string hexValues      = "0123456789ABCDEF";
                const string hexValuesLower = "0123456789abcdef";
                var          values         = true ? hexValues.AsSpan() : hexValuesLower.AsSpan();
            
                int srcIdx = 0;
                for (int i = 0; i < length; i += 2) {
                    buffer[i]     = values[hexBytes[srcIdx] >> 4];
                    buffer[i + 1] = values[hexBytes[srcIdx] & 0xF];
                    srcIdx++;
                }

                var result = buffer.ToString();
                ArrayPool<char>.Shared.Return(buffer);
                return result;
            }
            
            [Benchmark]
            public ReadOnlySpan<char> ToHexest2()
            {
                int          length         = hexBytes.Length * 2;
                
                var str = string.Create(length, (length, hexBytes.AsSpan().ToArray()), (chars, state) =>
                {
                    const string hexValues      = "0123456789ABCDEF";
                    const string hexValuesLower = "0123456789abcdef";
                    var          values         = true ? hexValues.AsSpan() : hexValuesLower.AsSpan();
    
                    int srcIdx = 0;
                    for (int i = 0; i < state.length; i += 2) {
                        chars[i]     = values[state.Item2[srcIdx] >> 4];
                        chars[i + 1] = values[state.Item2[srcIdx] & 0xF];
                        srcIdx++;
                    }
                });
            
                return str.AsSpan();
            }
        }
    }
}