using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FauFau.Net.Web;

namespace Tests
{
    class Program
    {
        static void Main(string[] args)
        {
            //Red5SigTests.Test1();
            
            //GtChunksTests.TestLoad();

            //AuthTests.VerifyBench();
            
            //AuthTests.SecretBench();
            
            AuthTests.UserIdBench();
        }
    }
}