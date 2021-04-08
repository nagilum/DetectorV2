using System;

namespace DetectorWorker.Exceptions
{
    public class InvalidConnectingIpException : Exception
    {
        public string ConnectingIp { get; set; }

        public InvalidConnectingIpException() { }

        public InvalidConnectingIpException(string message, string connectingIp) : base(message)
        {
            this.ConnectingIp = connectingIp;
        }

        public InvalidConnectingIpException(string message, Exception inner, string connectingIp) : base(message, inner)
        {
            this.ConnectingIp = connectingIp;
        }
    }
}