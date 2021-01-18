using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FauFau.Net.Web;

namespace Tests
{
    public static class AuthTests
    {
        public static void VerifyBench()
        {
            string secret = "36e3788836c2c0c2335d6e7b96220fe1fac1a898";
            string header = "Red5 f835fb24da4592d417a93e43868939d8688a87e2 ver=2&tc=1610833076&nonce=3e691feb538a38a2&uid=Qth4CwFkTyixv3NPM6V8RL4BByY%3D&host=oracleweb-testserver.nyaasync.net&path=%2Fclientapi%2Fapi%2Fv1%2Flogin_alerts&hbody=da39a3ee5e6b4b0d3255bfef95601890afd80709&cid=0";
            
            Console.WriteLine("Valid " + Auth.Verify(secret, header));

            int iterations = 1_000_000;

            Stopwatch sw = Stopwatch.StartNew();
            
            while (true)
            {
                sw.Restart();
                
                for (int i = 0; i < iterations; i++)
                {
                    Auth.Verify(secret, header);
                }
                Console.WriteLine($"{iterations} sequential verifications in {sw.ElapsedMilliseconds} ms");

                sw.Restart();

                Parallel.For(0, iterations*10, i =>
                {
                    Auth.Verify(secret, header);
                });
                Console.WriteLine($"{iterations * 10} threaded verifications in {sw.ElapsedMilliseconds} ms");
            }
        }
        
        public static void SecretBench()
        {
            string mail = "test@mail.com";
            string pass = "password";
            
            Console.WriteLine($"Secret v1 - {Auth.GenerateSecret(mail, pass, false).ToString()}");
            Console.WriteLine($"Secret v2 - {Auth.GenerateSecret(mail, pass, true).ToString()}");

            int iterations = 1_000_000;

            Stopwatch sw = Stopwatch.StartNew();
            
            while (true)
            {
                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    Auth.GenerateSecret(mail, pass, false);
                }
                Console.WriteLine($"{iterations} sequential secret v1 generations in {sw.ElapsedMilliseconds} ms");

                sw.Restart();
                Parallel.For(0, iterations * 10, i =>
                {
                    Auth.GenerateSecret(mail, pass, false);
                });
                Console.WriteLine($"{iterations * 10} threaded secret v1 genrations in {sw.ElapsedMilliseconds} ms");
                
                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    Auth.GenerateSecret(mail, pass, true);
                }
                Console.WriteLine($"{iterations} sequential secret v2 generations in {sw.ElapsedMilliseconds} ms");

                sw.Restart();
                Parallel.For(0, iterations * 10, i =>
                {
                    Auth.GenerateSecret(mail, pass, true);
                });
                Console.WriteLine($"{iterations * 10} threaded secret v2 genrations in {sw.ElapsedMilliseconds} ms");
            }
        }
        
        public static void UserIdBench()
        {
            string mail = "test@mail.com";
            
            Console.WriteLine("UserId " + Auth.GenerateUserId(mail).ToString());

            int iterations = 1_000_000;

            Stopwatch sw = Stopwatch.StartNew();
            
            while (true)
            {
                sw.Restart();
                
                for (int i = 0; i < iterations; i++)
                {
                    Auth.GenerateUserId(mail);
                }
                Console.WriteLine($"{iterations} sequential user ids generated in {sw.ElapsedMilliseconds} ms");

                sw.Restart();

                Parallel.For(0, iterations*10, i =>
                {
                    Auth.GenerateUserId(mail);
                });
                Console.WriteLine($"{iterations * 10} threaded user ids generated in {sw.ElapsedMilliseconds} ms");
            }
        }
        
    }
}