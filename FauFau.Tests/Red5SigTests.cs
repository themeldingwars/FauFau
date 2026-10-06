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
            sig.Cid.ShouldBe(7U);
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
        public void GenerateUserId_MatchesAuthIgnoringCase()
        {
            string expected = Auth.GenerateUserId("test@mail.com").ToString();

            string userId = Red5Sig.GenerateUserId("Test@Mail.com").ToString();

            userId.ShouldBe(expected);
        }

        [TestMethod]
        public void GenerateUserId_UrlEncode_EncodesBase64()
        {
            string userId = Red5Sig.GenerateUserId("test@mail.com", true).ToString();

            userId.ShouldBe("Qth4CwFkTyixv3NPM6V8RL4BByY%3d");
        }

        [TestMethod]
        public void GenerateSecret_MatchesAuthV1()
        {
            string expected = Auth.GenerateSecret("test@mail.com", "password", false).ToString();

            string secret = Red5Sig.GenerateSecret("test@mail.com", "password").ToString();

            secret.ShouldBe(expected);
        }

        [TestMethod]
        public void GenerateToken_MatchesKnownValue()
        {
            string token = Red5Sig.GenerateToken(Secret, Request).ToString();

            token.ShouldBe(Token);
        }
    }
}
