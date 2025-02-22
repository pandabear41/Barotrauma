﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace Barotrauma.Networking
{
    public enum NetworkConnectionStatus
    {
        Connected = 0x1,
        Disconnected = 0x2
    }

    public abstract class NetworkConnection
    {
        public const double TimeoutThreshold = 60.0; //full minute for timeout because loading screens can take quite a while

        public string Name;

        public UInt64 SteamID
        {
            get;
            protected set;
        }

        public string EndPointString
        {
            get;
            protected set;
        }

        public NetworkConnectionStatus Status = NetworkConnectionStatus.Disconnected;
    }
}
