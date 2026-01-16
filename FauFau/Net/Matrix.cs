using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FauFau.Net
{
    public class Matrix
    {
        public class Server
        {
            public delegate void PacketHandler(ref byte[] bytes);
            public delegate void OnDisconnect();

            private Thread thread;
            private volatile bool running;

            public void Start()
            {
                if(thread == null)
                {
                    thread = new Thread(Run);
                    thread.Start();
                }
            }

            public void Stop()
            {
                if (thread.IsAlive)
                {
                    running = false;
                }
            }
            private void Run()
            {

            }

            private class Worker
            {
                private string serverAddress;
                private int serverPort;
                private int _localPort;
                private PacketHandler fromClient;
                private PacketHandler fromServer;
                private volatile bool stop;
                private UdpClient udpClient;
                private int? localPort = null;

                public Worker(string serverAddress, int serverPort, int localPort, PacketHandler fromClient, PacketHandler fromServer)
                {
                    this.serverAddress = serverAddress;
                    this.serverPort = serverPort;
                    this._localPort = localPort;
                    this.fromClient = fromClient;
                    this.fromServer = fromServer;
                }

                public void Start()
                {
                    this.udpClient = new UdpClient(_localPort);
                    while (!stop)
                    {
                        try
                        {
                            IPEndPoint endPoint = null;
                            byte[] bytes = udpClient.Receive(ref endPoint);

                            if (localPort == null)
                            {
                                localPort = endPoint.Port;
                            }
                            if (IPAddress.IsLoopback(endPoint.Address))
                            {
                                byte[] b = bytes;
                                fromClient(ref b);
                                if (b != null)
                                {
                                    bytes = b;
                                }
                                udpClient.Send(bytes, bytes.Length, serverAddress, serverPort);
                            }
                            else
                            {
                                byte[] b = bytes;
                                fromServer(ref b);
                                if (b != null)
                                {
                                    bytes = b;
                                }
                                udpClient.Send(bytes, bytes.Length, "127.0.0.1", localPort.Value);
                            }

                        }
                        catch (SocketException ex)
                        {
                            //Console.WriteLine(ex);
                            //Console.WriteLine(ex);

                            // on disconenct
                        }
                    }
                }
                public void Stop()
                {
                    stop = true;
                }
            }
        }
    }
}
