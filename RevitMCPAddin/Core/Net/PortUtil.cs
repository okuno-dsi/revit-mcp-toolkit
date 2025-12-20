// Core/Net/PortUtil.cs
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace RevitMCPAddin.Core.Net
{
    public static class PortUtil
    {
        public static bool IsPortFree(int port)
        {
            try
            {
                TcpListener l = new TcpListener(IPAddress.Loopback, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        public static int FindNextFree(int basePort, int maxTry = 50)
        {
            for (int i = 0; i < maxTry; i++)
            {
                int p = basePort + i;
                if (IsPortFree(p)) return p;
            }
            return -1;
        }
    }
}
