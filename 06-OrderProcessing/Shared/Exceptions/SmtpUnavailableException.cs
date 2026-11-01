namespace _06_OrderProcessing.Shared.Exceptions;

public class SmtpUnavailableException : Exception
{
    public SmtpUnavailableException(string message) : base(message) { }
}
