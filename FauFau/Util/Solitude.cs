using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace FauFau.Util
{
    public class Solitude : IDisposable
    {
        public delegate void CmdDelegate(List<string> commands);

        private Mutex mutex;
        private string name;
        private bool createdNew;
        private bool aquiredMutex;
        private NamedPipeServerStream pipeServerSteam;
        private readonly object pipeLock = new object();
        private CmdDelegate cmdHandler;

        public bool FirstInstance { get => aquiredMutex; }

        public Solitude(string name, CmdDelegate commandHandler = null)
        {
            this.name = name;
            this.cmdHandler = commandHandler;

            // try to open existing mutex, if it dosn't we create a new globally accessible one
            if (!Mutex.TryOpenExisting(name + ".mutex", out mutex))
            {
                mutex = new Mutex(false, name + ".mutex", out createdNew);
            }

            // try to aquire the mutex
            try
            {
                // wait half a second in case the mutex is contended
                aquiredMutex = mutex.WaitOne(500, false);
            }
            catch (AbandonedMutexException)
            {
                // try to reaquire abandoned mutex
                mutex.ReleaseMutex();
                aquiredMutex = mutex.WaitOne();
            }

            if (aquiredMutex)
            {
                GC.KeepAlive(mutex);
                CreateNamedPipeServer();
                cmdHandler?.Invoke(Environment.GetCommandLineArgs().ToList());
            }
            else
            {
                SendCmdArgs();
            }
        }


        private void CreateNamedPipeServer()
        {
            pipeServerSteam = new NamedPipeServerStream(name + ".pipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            pipeServerSteam.BeginWaitForConnection(NamedPipeServerConnectionCallback, pipeServerSteam);
        }

        private void NamedPipeServerConnectionCallback(IAsyncResult result)
        {
            try
            {
                pipeServerSteam.EndWaitForConnection(result);
                lock (pipeLock)
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CmdPayload));
                    var payload = (CmdPayload)serializer.ReadObject(pipeServerSteam);
                    cmdHandler?.Invoke(payload.CommandLineArguments);
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception) { }
            finally
            {
                pipeServerSteam.Dispose();
            }
            CreateNamedPipeServer();
        }

        private void SendCmdArgs()
        {
            // pass on cmd line args
            try
            {
                using (NamedPipeClientStream pipeClientStream = new NamedPipeClientStream(".", name + ".pipe", PipeDirection.Out))
                {
                    pipeClientStream.Connect(2000);

                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CmdPayload));

                    CmdPayload payload = new CmdPayload();
                    payload.CommandLineArguments = Environment.GetCommandLineArgs().ToList();

                    serializer.WriteObject(pipeClientStream, payload);
                }
            }
            catch (Exception ex)
            {
                // dun really care
            }
        }

        [DataContract]
        public class CmdPayload
        {
            [DataMember]
            public List<string> CommandLineArguments { get; set; } = new List<string>();
        }

        public void Dispose()
        {
            try
            {

                mutex.ReleaseMutex();
                mutex.Dispose();
            }
            catch (Exception)
            {

            }

        }
    }
}
