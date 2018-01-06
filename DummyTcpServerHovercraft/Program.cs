using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class Program
{
    // Thread signal.
    private static ManualResetEvent _mreSignal = new ManualResetEvent(false);

    public static void Main(String[] args)
    {
        // Create a local TCP/IP stream socket.
        var ipAddress = new IPAddress(new byte[] { 127, 0, 0, 1 });
        var localEndPoint = new IPEndPoint(ipAddress, 8077);

        var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        /* With nodelay set to true, Nagle will be disabled.
        The Nagle algorithm is intended to reduce TCP / IP traffic of small packets sent over the network
        by combining a number of small outgoing messages, and sending them all at once.
        The downside of such approach is delaying individual messages until a big enough packet is assembled. */
        listener.NoDelay = true;

        try
        {
            listener.Bind(localEndPoint);
            listener.Listen(1);

            while (true)
            {
                _mreSignal.Reset();

                // Start an asynchronous socket to listen for connections.
                listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);

                _mreSignal.WaitOne();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }

        Console.WriteLine("\nPress any key...");
        Console.Read();
    }

    private static void AcceptCallback(IAsyncResult ar)
    {
        try
        {
            // Signal the main thread to continue.
            _mreSignal.Set();

            // Get the socket that handles the client request.
            Socket listener = (Socket)ar.AsyncState;
            Socket handler = listener.EndAccept(ar);
            handler.NoDelay = true;
        
            Int32 _lastSecAckCounter = 0;

            byte[] readBuffer = new byte[256];
            byte[] writeBuff = new byte[1024];

            StringBuilder sb = new StringBuilder();

            while (true)
            {
                int len = handler.Receive(readBuffer);

                Interlocked.Increment(ref _lastSecAckCounter);

                sb.Clear();
                sb.Append($"ACK #{_lastSecAckCounter} {len} bytes");

                if (len > 0)
                {
                    sb.Append(" : ");
                    for (var i = 0; i<len; i++)
                    {
                        sb.Append(readBuffer[i]);
                        sb.Append(" ");
                    }
                }

                sb.AppendLine("");

                var ackText = sb.ToString();

                Array.Clear(writeBuff, 0, writeBuff.Length);
                len = Encoding.ASCII.GetBytes(ackText, 0, sb.Length, writeBuff, 0);
                handler.Send(writeBuff, len, SocketFlags.None);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }
}