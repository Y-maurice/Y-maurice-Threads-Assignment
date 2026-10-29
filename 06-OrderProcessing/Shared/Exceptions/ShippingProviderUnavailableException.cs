namespace _06_OrderProcessing.Shared.Exceptions;

public class ShippingProviderUnavailableException : Exception
{
    public ShippingProviderUnavailableException(string message) : base(message) { }
}
