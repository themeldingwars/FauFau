using BenchmarkDotNet.Attributes;
using FauFau.Net.Web;

namespace FauFau.Benchmarks
{
    [MemoryDiagnoser]
    [ShortRunJob]
    public class Red5SigBenchmarks
    {
        private const string HeaderV1 = @"Red5 669d417a26d056693a63f3c63437be717febb615 ver=1&tc=1464664242&nonce=0000295c00007084&uid=d8wP5gy2K%2BrsJYAi8PKf%2BJq4Bv8%3D&host=DUMPTRUCK&path=&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709";
        private const string HeaderV2 = @"Red5 a9c343fd0dabef5d39a7sg75bf7c23e0bd19c477 ver=2&tc=1499479803&nonce=08367a9b7e102ac7&uid=v5W1FUHSSHL9ZXZ3LYuH0VapzX4%3D&host=clientapi-v01-sna01-prod.firefall.com&path=%2Fapi%2Fv2%2Faccounts%2Flogin&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0 4d43e3eab7b67f7bb7c47e031dc63f5506fec77b";
        private const string HeaderGarbage = @"ixslfcjlbpujzrpjzzlfaxtkdqzqbdogvzeiqnazdtffkvycidcwilvkduysurxhvczvaghphihltpujbmenogptyszlxvdrtqholvowjncmmlujpiznhybjgsnnmtzf tEUob7sLYoKU=CeBZu&& ==Xa0Ab6Mkk9GoLZ8BTVhVeVks0Ii8GNurWJDhboRiLRqGgFwUl5xjEACogCkcKLekdNHSa2IzUylDcG9RTOfhbcWGpMkHBWcSugQpdSCu8Bsde87";
        private const string HeaderGarbageVersion = @"red5 ver=ixslfcjlbpujzrpjzzlfaxtkdqzqbdogvzeiqnazdtffkvycidcwilvkduysurxhvczvaghphihltpujbmenogptyszlxvdrtqholvowjncmmlujpiznhybjgsnnmtzf tEUob7sLYoKU=CeBZu&& ==Xa0Ab6Mkk9GoLZ8BTVhVeVks0Ii8GNurWJDhboRiLRqGgFwUl5xjEACogCkcKLekdNHSa2IzUylDcG9RTOfhbcWGpMkHBWcSugQpdSCu8Bsde87";
        private const string Email = @"gdsgsdg@test.com";
        private const string Password = @"dgdssdgsdgsgfgdfg";

        [Benchmark]
        public int ParseV1()
        {
            return Red5Sig.ParseString(HeaderV1).Version;
        }

        [Benchmark]
        public int ParseV2()
        {
            return Red5Sig.ParseString(HeaderV2).Version;
        }

        [Benchmark]
        public int ParseGarbage()
        {
            return Red5Sig.ParseString(HeaderGarbage).Version;
        }

        [Benchmark]
        public int ParseGarbageVersion()
        {
            return Red5Sig.ParseString(HeaderGarbageVersion).Version;
        }

        [Benchmark]
        public int GenerateUserId()
        {
            return Red5Sig.GenerateUserId(Email).Length;
        }

        [Benchmark]
        public int GenerateSecret()
        {
            return Red5Sig.GenerateSecret(Email, Password).Length;
        }
    }
}
