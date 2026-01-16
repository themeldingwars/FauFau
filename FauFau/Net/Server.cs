using System.Threading;

namespace FauFau.Net
{
    public class Server
    {
        private bool running = false;
        private Thread matrixThread;
        //private MatrixWorker matrixWorker;

        public void Start(int matrixPort = Constants.DefaultMatrixPort, int gamePort = Constants.DefaultGamePort)
        {
            if(running)
            {
                // err
                return;
            }

            running = true;

        }

        public void Stop()
        {

        }
    }
}
