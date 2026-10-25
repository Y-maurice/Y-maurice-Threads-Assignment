namespace TableBooking;

public sealed class FridayNightBooking
{
    private readonly Lock _lock = new Lock();
    public int TablesLeft { get; private set; } = 5;
    public int Booked { get; private set; }

    public void Book(string customerName)
    {
        lock (_lock)
        {
            if (TablesLeft <= 0)
                return;

            Thread.Sleep(20);

            TablesLeft--;
            Booked++;
            Console.WriteLine($"{customerName} booked a table. Left={TablesLeft}");
        }
    }
}
