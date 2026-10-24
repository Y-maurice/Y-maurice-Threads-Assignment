# Assignment 01 — Answers

- Student name: Youssef Maurice 
- Group / ID: sunday & wednesday

## Problems with the current design

1. **Orders are processed one after another.** The `foreach` loop finishes
   load order, read customer, write invoice for order 1001 before it even
   starts 1002. The orders don't depend on each other, so this waiting is
   unnecessary. My first run took 5.9 s, which matches
   800 ms + 5 × (400 + 300 + 300) ms. The time grows linearly with the number
   of orders, so with many orders the export would be very slow.

2. **Blocking calls hold the thread.** `Thread.Sleep` in `OrderDatabase`,
   `CustomerFileStore` and `InvoiceFileWriter`, and `File.ReadAllText` and
   `File.WriteAllText`, make the thread sit idle while it waits for I/O. It
   does no useful work during those waits, and with many orders it would tie
   up threads that other work could use.

## What you changed

**Each class became async.**
- `OrderDatabase`: both methods now return `Task` and use `await Task.Delay`
  instead of `Thread.Sleep`.
- `CustomerFileStore`: `ReadProfileAsync` uses `await Task.Delay(300)` and
  `await File.ReadAllTextAsync(path)`.
- `InvoiceFileWriter`: `WriteInvoiceAsync` uses `await Task.Delay(300)` and
  `await File.WriteAllTextAsync(path, body)`.
- All methods got the `Async` suffix.

**The loop in `InvoiceExportService` was redesigned.**
Using `await` inside the `foreach` would only stop blocking the thread; the
orders would still run one by one and take about 5.9 s. So I did this:
1. Moved the three steps into `ExportOrderAsync(order)`. Inside it they stay
   in sequence, because each needs the previous result.
2. **Start phase:** a loop calls `ExportOrderAsync` for every order without
   awaiting and stores the tasks in a list, so all five pipelines run at the
   same time.
3. **Await phase:** a second loop awaits the tasks one by one and prints
   `Wrote ...`. Because I await the list in order, the output lines appear in
   the same order as before.
