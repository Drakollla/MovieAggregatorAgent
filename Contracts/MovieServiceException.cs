namespace Contracts
{
    public class MovieServiceException : Exception
    {
        public MovieServiceException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
