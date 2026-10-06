using BenchmarkDotNet.Attributes;
using FauFau.Net.Web;

namespace FauFau.Benchmarks
{
    [MemoryDiagnoser]
    [ShortRunJob]
    public class AuthBenchmarks
    {
        private const string Secret = "36e3788836c2c0c2335d6e7b96220fe1fac1a898";
        private const string Header = "Red5 f835fb24da4592d417a93e43868939d8688a87e2 ver=2&tc=1610833076&nonce=3e691feb538a38a2&uid=Qth4CwFkTyixv3NPM6V8RL4BByY%3D&host=oracleweb-testserver.nyaasync.net&path=%2Fclientapi%2Fapi%2Fv1%2Flogin_alerts&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0";
        private const string Email = "test@mail.com";
        private const string Password = "password";

        [Benchmark]
        public bool Verify()
        {
            return Auth.Verify(Secret, Header);
        }

        [Benchmark]
        public int GenerateSecretV1()
        {
            return Auth.GenerateSecret(Email, Password, false).Length;
        }

        [Benchmark]
        public int GenerateSecretV2()
        {
            return Auth.GenerateSecret(Email, Password, true).Length;
        }

        [Benchmark]
        public int GenerateUserId()
        {
            return Auth.GenerateUserId(Email).Length;
        }
    }
}
