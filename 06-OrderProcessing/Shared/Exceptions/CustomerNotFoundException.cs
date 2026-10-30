namespace _06_OrderProcessing.Shared.Exceptions;

public class CustomerNotFoundException : Exception
{
    public CustomerNotFoundException(string message) : base(message) { }
}
