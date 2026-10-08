using System;
using System.Text;
using FauFau.Net.Web;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class Red5SigTests
    {
        private const string Secret = "36e3788836c2c0c2335d6e7b96220fe1fac1a898";
        private const string Request = "ver=2&tc=1610833076&nonce=3e691feb538a38a2&uid=Qth4CwFkTyixv3NPM6V8RL4BByY%3D&host=oracleweb-testserver.nyaasync.net&path=%2Fclientapi%2Fapi%2Fv1%2Flogin_alerts&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0";
        private const string Token = "f835fb24da4592d417a93e43868939d8688a87e2";
        private const string Host = "oracleweb-testserver.nyaasync.net";
        private const string Path = "/clientapi/api/v2/accounts/login";
        private const uint SignedAt = 1610833076;
        private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"email\":\"test@mail.com\"}");

        private static string SignedHeader(byte[] body, string host = Host, string path = Path)
        {
            string request = Red5Sig.CreateRequestString("Qth4CwFkTyixv3NPM6V8RL4BByY=", host, path, Red5Sig.HashBody(body), 0, SignedAt, "3e691feb538a38a2").ToString();
            char[] token = new char[40];
            Auth.GenerateToken(Secret, request, token);
            return "Red5 " + new string(token) + " " + request + " " + Token;
        }

        private static Red5Sig.VerifyResult Verify(string header, byte[] body, uint now = SignedAt, string host = Host, string path = Path)
        {
            return Red5Sig.Verify(Secret, header, body, DateTimeOffset.FromUnixTimeSeconds(now), TimeSpan.FromMinutes(5), host, path);
        }

        [TestMethod]
        public void ParseString_V2Header_ReadsAllFields()
        {
            string header = "Red5 " + Token + " " + Request.Replace("cid=0", "cid=7") + " trailing";

            Red5Sig.QsValues sig = Red5Sig.ParseString(header);

            sig.Token.ToString().ShouldBe(Token);
            sig.Token2.ToString().ShouldBe("trailing");
            sig.Version.ShouldBe(2);
            sig.Time.ShouldBe(1610833076U);
            sig.Nonce.ToString().ShouldBe("3e691feb538a38a2");
            sig.UID.ToString().ShouldBe("Qth4CwFkTyixv3NPM6V8RL4BByY%3D");
            sig.Host.ToString().ShouldBe("oracleweb-testserver.nyaasync.net");
            sig.Path.ToString().ShouldBe("/clientapi/api/v1/login_alerts");
            sig.Body.ToString().ShouldBe("da39a3ee5e6b4b0d3255bfef95601890afd80709");
            sig.Cid.ShouldBe(7UL);
        }

        [TestMethod]
        public void ParseString_CharacterGuid_ReadsFullCid()
        {
            string header = "Red5 " + Token + " " + Request.Replace("cid=0", "cid=18446744073709551615");

            Red5Sig.QsValues sig = Red5Sig.ParseString(header);

            sig.Cid.ShouldBe(ulong.MaxValue);
        }

        [TestMethod]
        public void ParseString_NotRed5_ReturnsEmpty()
        {
            Red5Sig.QsValues sig = Red5Sig.ParseString("Bearer abc ver=2");

            sig.Version.ShouldBe(0);
            sig.Token.IsEmpty.ShouldBeTrue();
        }

        [TestMethod]
        public void ParseString_KeyWithoutValue_IsSkipped()
        {
            Red5Sig.QsValues sig = Red5Sig.ParseString("red5 abc ver=1&broken&tc=12");

            sig.Version.ShouldBe(1);
            sig.Time.ShouldBe(12U);
        }

        [TestMethod]
        public void CreateRequestString_MatchesClientRequest()
        {
            const string uid = "Qth4CwFkTyixv3NPM6V8RL4BByY=";
            const string host = "oracleweb-testserver.nyaasync.net";
            const string path = "/clientapi/api/v1/login_alerts";
            const string hbody = "da39a3ee5e6b4b0d3255bfef95601890afd80709";

            string request = Red5Sig.CreateRequestString(uid, host, path, hbody, 0, 1610833076, "3e691feb538a38a2").ToString();

            request.ShouldBe(Request);
        }

        [TestMethod]
        public void CreateRequestString_Now_UsesSecondsAndLongNonce()
        {
            uint before = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string request = Red5Sig.CreateRequestString("uid", "host", "/path", "hbody", 9197696484326682622).ToString();
            Red5Sig.QsValues sig = Red5Sig.ParseString("Red5 " + Token + " " + request);

            sig.Time.ShouldBeInRange(before, before + 5);
            sig.Nonce.Length.ShouldBe(16);
            sig.Cid.ShouldBe(9197696484326682622UL);
        }

        [TestMethod]
        public void HashBody_EmptyBody_MatchesClientHash()
        {
            string hbody = Red5Sig.HashBody(ReadOnlySpan<byte>.Empty);

            hbody.ShouldBe("da39a3ee5e6b4b0d3255bfef95601890afd80709");
        }

        [TestMethod]
        public void Verify_ClientRequest_IsValid()
        {
            Red5Sig.VerifyResult result = Red5Sig.Verify(Secret, "Red5 " + Token + " " + Request, ReadOnlySpan<byte>.Empty,
                                                         DateTimeOffset.FromUnixTimeSeconds(1610833076), TimeSpan.FromMinutes(5),
                                                         "oracleweb-testserver.nyaasync.net", "/clientapi/api/v1/login_alerts");

            result.ShouldBe(Red5Sig.VerifyResult.Valid);
        }

        [TestMethod]
        public void Verify_SignedRequest_IsValid()
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Verify(header, Body);

            result.ShouldBe(Red5Sig.VerifyResult.Valid);
        }

        [TestMethod]
        public void Verify_WithoutHostAndPath_SkipsThem()
        {
            string header = SignedHeader(Body, "other.host", "/other");

            Red5Sig.VerifyResult result = Verify(header, Body, host: null, path: null);

            result.ShouldBe(Red5Sig.VerifyResult.Valid);
        }

        [TestMethod]
        public void Verify_NotRed5_IsMalformed()
        {
            Red5Sig.VerifyResult result = Verify("Bearer abc", Body);

            result.ShouldBe(Red5Sig.VerifyResult.Malformed);
        }

        [TestMethod]
        public void Verify_WrongSecret_IsInvalidToken()
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Red5Sig.Verify("575c232a4939112747ef5480378be3f39f6ff547", header, Body,
                                                         DateTimeOffset.FromUnixTimeSeconds(SignedAt), TimeSpan.FromMinutes(5));

            result.ShouldBe(Red5Sig.VerifyResult.InvalidToken);
        }

        [TestMethod]
        public void Verify_ChangedSignedValue_IsInvalidToken()
        {
            string header = SignedHeader(Body).Replace("tc=" + SignedAt, "tc=" + (SignedAt + 1));

            Red5Sig.VerifyResult result = Verify(header, Body);

            result.ShouldBe(Red5Sig.VerifyResult.InvalidToken);
        }

        [TestMethod]
        public void Verify_ChangedBody_IsBodyMismatch()
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Verify(header, Encoding.UTF8.GetBytes("{}"));

            result.ShouldBe(Red5Sig.VerifyResult.BodyMismatch);
        }

        [TestMethod]
        [DataRow(-301L)]
        [DataRow(301L)]
        public void Verify_OutsideClockSkew_IsExpired(long offset)
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Verify(header, Body, (uint)(SignedAt + offset));

            result.ShouldBe(Red5Sig.VerifyResult.Expired);
        }

        [TestMethod]
        public void Verify_InsideClockSkew_IsValid()
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Verify(header, Body, SignedAt + 300);

            result.ShouldBe(Red5Sig.VerifyResult.Valid);
        }

        [TestMethod]
        public void Verify_OtherHost_IsHostMismatch()
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Verify(header, Body, host: "evil.host");

            result.ShouldBe(Red5Sig.VerifyResult.HostMismatch);
        }

        [TestMethod]
        public void Verify_OtherPath_IsPathMismatch()
        {
            string header = SignedHeader(Body);

            Red5Sig.VerifyResult result = Verify(header, Body, path: "/clientapi/api/v2/accounts/change_password");

            result.ShouldBe(Red5Sig.VerifyResult.PathMismatch);
        }
    }
}