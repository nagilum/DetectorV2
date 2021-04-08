using System;

namespace DetectorWorker.Exceptions
{
    public class SslException : Exception
    {
        public string Code { get; set; }

        public SslException() { }

        public SslException(string message, string code) : base(message)
        {
            this.Code = code;
        }

        public SslException(string message, Exception inner, string code) : base(message, inner)
        {
            this.Code = code;
        }
    }
}