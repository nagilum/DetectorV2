using System;

namespace DetectorWorker.Exceptions
{
    public class InvalidHttpStatusCodeException : Exception
    {
        public int HttpStatusCode { get; set; }

        public InvalidHttpStatusCodeException() { }

        public InvalidHttpStatusCodeException(string message, int httpStatusCode) : base(message)
        {
            this.HttpStatusCode = httpStatusCode;
        }

        public InvalidHttpStatusCodeException(string message, Exception inner, int httpStatusCode) : base(message, inner)
        {
            this.HttpStatusCode = httpStatusCode;
        }
    }
}