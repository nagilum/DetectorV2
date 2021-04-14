using System;

namespace DetectorWorker.Exceptions
{
    public class UnableToResolveIpException : Exception
    {
        public UnableToResolveIpException() { }

        public UnableToResolveIpException(string message) : base(message) { }

        public UnableToResolveIpException(string message, Exception inner) : base(message, inner) { }
    }
}