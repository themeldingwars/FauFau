using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FauFau.Net.Web;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class AuthTests
    {
        private const string Email = "test@mail.com";
        private const string Password = "password";
        private const string UserId = "Qth4CwFkTyixv3NPM6V8RL4BByY=";
        private const string SecretV1 = "575c232a4939112747ef5480378be3f39f6ff547";
        private const string SecretV2 = "36e3788836c2c0c2335d6e7b96220fe1fac1a898";
        private const string Request = "ver=2&tc=1610833076&nonce=3e691feb538a38a2&uid=Qth4CwFkTyixv3NPM6V8RL4BByY%3D&host=oracleweb-testserver.nyaasync.net&path=%2Fclientapi%2Fapi%2Fv1%2Flogin_alerts&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0";
        private const string Token = "f835fb24da4592d417a93e43868939d8688a87e2";
        private const string Header = "Red5 " + Token + " " + Request;

        private static string ReferenceToken(string secret, string request)
        {
            return Convert.ToHexString(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(request))).ToLowerInvariant();
        }

        private static string GenerateToken(string secret, string request)
        {
            char[] token = new char[40];
            Auth.GenerateToken(secret, request, token);
            return new string(token);
        }

        [TestMethod]
        [DataRow("test@mail.com")]
        [DataRow("Test@MAIL.com")]
        public void GenerateUserId_MatchesKnownValueIgnoringCase(string email)
        {
            string userId = Auth.GenerateUserId(email).ToString();

            userId.ShouldBe(UserId);
        }

        [TestMethod]
        public void GenerateUserId_NonAsciiEmail_HashesUtf8()
        {
            const string email = "jürgen@mail.com";
            string expected = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(email + Auth.USER_ID_SALT)));

            string userId = Auth.GenerateUserId(email).ToString();

            userId.ShouldBe(expected);
        }

        [TestMethod]
        [DataRow(false, SecretV1)]
        [DataRow(true, SecretV2)]
        public void GenerateSecret_MatchesKnownValues(bool v2, string expected)
        {
            string secret = Auth.GenerateSecret(Email, Password, v2).ToString();

            secret.ShouldBe(expected);
        }

        [TestMethod]
        public void GenerateSecret_LongInput_DoesNotThrow()
        {
            string password = new string('x', 2000);
            string expected = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Email + "-" + password + Auth.USER_AUTH_SALT))).ToLowerInvariant();

            string secret = Auth.GenerateSecret(Email, password, false).ToString();

            secret.ShouldBe(expected);
        }

        [TestMethod]
        public void GenerateToken_MatchesKnownValue()
        {
            string token = GenerateToken(SecretV2, Request);

            token.ShouldBe(Token);
        }

        [TestMethod]
        public void GenerateToken_RepeatedCalls_MatchHmacSha1()
        {
            string[] requests = Enumerable.Range(0, 5).Select(i => Request + "&n=" + i).ToArray();

            string[] tokens = requests.Select(r => GenerateToken(SecretV2, r)).ToArray();

            tokens.ShouldBe(requests.Select(r => ReferenceToken(SecretV2, r)));
        }

        [TestMethod]
        public void GenerateToken_LongRequest_MatchesHmacSha1()
        {
            string request = Request + "&pad=" + new string('a', 3000);

            string token = GenerateToken(SecretV2, request);

            token.ShouldBe(ReferenceToken(SecretV2, request));
        }

        [TestMethod]
        public void Verify_KnownHeader_SucceedsEveryTime()
        {
            bool[] results = Enumerable.Range(0, 3).Select(_ => Auth.Verify(SecretV2, Header)).ToArray();

            results.ShouldAllBe(verified => verified);
        }

        [TestMethod]
        public void Verify_WrongSecret_Fails()
        {
            bool verified = Auth.Verify(SecretV1, Header);

            verified.ShouldBeFalse();
        }

        [TestMethod]
        [DataRow("Red5 tooshort")]
        [DataRow("Bearer " + Token + " " + Request)]
        public void Verify_MalformedHeader_Fails(string header)
        {
            bool verified = Auth.Verify(SecretV2, header);

            verified.ShouldBeFalse();
        }

        [TestMethod]
        public void Sign_WritesTokenThatVerifies()
        {
            char[] header = ("Red5 " + new string('0', 40) + " " + Request).ToCharArray();

            bool signed = Auth.Sign(SecretV2, header);

            signed.ShouldBeTrue();
            new string(header).ShouldBe(Header);
        }
    }
}
