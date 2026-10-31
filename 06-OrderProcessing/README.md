## Phase 1 questions

### 1. Why are the sync and async times almost the same when orders are awaited one after another?

The async version only stops blocking the thread. It doesn't make anything
run at the same time. In `Phase1Runner` I validate the orders in a `foreach`
and `await` each one, so order #2 cannot start until order #1 is completely
finished. In my log, order #1 is saved at 15:13.597 and order #2 starts
(`CustomerRepository -> Opening connection...`) at 15:13.601, right after it.
The total is still the sum of every delay, exactly as in the sync run:
about 1.7 s of delays per order, times 20 orders.

My measured times were: sync 35.3 s, async 53.5 s. The async time is longer
because its stopwatch also includes the 20 saves (about 0.9 s each, about
18 s in total), and the sync run saves nothing, as the task says. Validation
alone takes about the same time in both runs. 

### 2. Is the thread ID before an `await` always the same as after? Why?

No. In the sync run every line says `Thread 8`, because nothing ever frees the
thread. In the async run the ID changes after an `await`. For order #1:

```
[15:09.928] [Thread 11] CustomerRepository -> Opening connection...
[15:10.681] [Thread 12] CustomerRepository -> Customer #1 found (Ahmed Ali)
```

The reason is that a console app has no synchronization context. While
`Task.Delay` waits, no thread is held. When the delay finishes, the rest of
the method is scheduled on any free thread-pool thread, which may or may not
be the one that started it. It is sometimes the same (many consecutive lines
show `Thread 12`), so the ID can't be relied on, and my code never depends
on it.

### 3. What does the thread do while `Task.Delay()` is waiting? And while `Thread.Sleep()` is waiting?

- **`Task.Delay`:** nothing. The thread is not blocked. `await` returns the
  thread to the pool, so it can run other work. A timer completes the delay
  later, and the method continues on a pool thread.
- **`Thread.Sleep`:** the thread is blocked. It does no work and can't run
  anything else, and it still occupies a thread (and its memory) for the whole
  wait. In the sync classes (`SyncCustomerRepository` and the others) the
  thread sits idle through every 200 to 600 ms step.

In Phase 1 this makes no difference in time, because only one order runs at a
time. It matters when many orders run together, since blocked threads would
run out.

### 4. Why should a repository method return `Task<T>` instead of `T`?

The data isn't ready when the method returns, because it needs 500 ms or more
of simulated database time. `Task<T>` is a promise of a value that will arrive
later. A method that returned a plain `T` would have to wait for the result
before returning, which means blocking the thread (the sync version). With
`Task<T>` the caller can:

- `await` it, so the thread is freed while waiting,
- start several calls first and `await` them later, which Phase 2 needs,
- get an exception from the failed operation when it awaits,
- and pass the `CancellationToken` so the operation can be cancelled.

For example, `CustomerRepository.GetByIdAsync` returns `Task<Customer?>`.

### 5. What is the difference between `File.ReadAllText` and `File.ReadAllTextAsync`? Does the async version occupy a thread while the disk is reading?

`File.ReadAllText` blocks the calling thread until the whole file has been
read. `File.ReadAllTextAsync` returns a `Task<string>` immediately, and the
caller `await`s it. While the disk is working, the thread is not held. It is
free, and the method continues when the read has finished. So the async
version does not occupy a thread during the disk read.

In my mini project, `JsonFileStore.ReadAllAsync` uses `File.ReadAllTextAsync(path, ct)`,
and the sync twin `ReadAll` uses `File.ReadAllText`, which the sync
repositories call. With five tiny files the difference is invisible in my
timings. It matters when many tasks read files at once.




## Phase 2 questions

### 1. What is the difference between starting a task and awaiting it?

Calling an async method *starts* it: it runs until its first `await` and then
returns a `Task` straight away, while the work continues. Awaiting is where
the caller waits for that task to finish and gets its result or exception.
In my runner I started `ProcessValidatedOrderAsync` for all 15 validated
orders first. All 15 `ProductRepository -> Opening connection...` lines are
at 11:29:19, within 1 ms, and `task[0]` printed `WaitingForActivation`
immediately. Only later did the second loop `await` them one by one. If I had
awaited inside the first loop, each order would wait for the previous one.

### 2. Does every Task create a new thread? Does Task.Delay use a thread while waiting?

No. In my log, the 20 `CustomerRepository -> Opening connection...` lines at
11:29:15 were all started by `Thread 5`, so one thread started 20
operations. `Task.Delay` uses a timer, so no thread is held while waiting, and
the continuation runs on a free thread-pool thread afterwards. That is why 20
and 15 concurrent tasks only used about ten different thread ids in my logs.
`Task.Run` is different: it does use a pool thread to run the code.

### 3. What happens with .Result or .Wait() on an unfinished task? Why is it dangerous?

`.Result` and `.Wait()` block the calling thread until the task finishes, so
the thread can do nothing else (my `BlockingDemo` blocks for 2 s). A faulted
task throws an `AggregateException` and not the original exception. It is also
dangerous because the blocked thread may be the one the task needs to continue,
which deadlocks in UI or classic ASP.NET contexts, and with many callers it
uses up the thread pool. <My console demo shows the blocking but not a
deadlock, since a console app has no synchronization context.> `await` does
none of this.

### 4. What happens to an exception thrown inside a task that is never awaited?

It is stored in the task and nobody observes it, so it is lost silently.
In my run, `Shipping API failed for Order #7` was logged at 11:29:22.508, but
the runner only reported `Order #7: task Faulted - Shipping provider
unavailable` after it had awaited the earlier tasks, at about 26.3 s. If I had
never awaited that task, order #7 would never be marked `Failed`, and its
reserved stock would never be released. The same example shows that a task
holds its exception until it is awaited.

### 5. When should you use Task.Run, and when should you avoid it?

Use it for CPU-bound work that would otherwise keep the calling thread busy.
In `InvoiceGenerator`, the 20,000,000-iteration loop runs inside `Task.Run`
(about 0.25 s per invoice in my log). Avoid it for I/O and waiting such as
`Task.Delay`, database calls and file access, because they are already
asynchronous and don't need a thread. Wrapping them in `Task.Run` only uses a
pool thread to wait. That's why the file write in the same method is a plain
`await File.WriteAllTextAsync`.

### 6. What is the difference between Task and Task<T>?

`Task` represents work with no result, such as `OrderRepository.SaveAsync` and
`ProductRepository.ReleaseStockAsync`. `Task<T>` also gives a value when it is
awaited, such as `ReserveStockAsync` (`bool`), `CalculateSubTotalAsync`
(`decimal`) and `ProcessValidatedOrderAsync` (`bool`). Both can be awaited and
both carry an exception.

### 7. Why can a lock block not contain an await?

A `lock` is owned by one thread: the thread that took it must be the one that
releases it. After an `await`, the method may continue on a different thread,
so the lock could not be released correctly. The compiler rejects it. In
`JsonFileStore` I used `SemaphoreSlim` with `WaitAsync(ct)` instead, which can
be awaited and is not tied to a thread.

### 8. Why did the unprotected stock reservation oversell, and how did you prevent it?

`ReserveStockUnsafeAsync` reads the stock, checks it, waits, and then writes
the new value, with nothing protecting that whole sequence. Both orders read
stock `1` before either wrote, so both passed the check, and both wrote
`1 - 1 = 0`. My demo printed `Order #10 got the keyboard: True`,
`Order #11 got the keyboard: True` and stock `0`. The bug even hid itself,
because the stock never went negative. The safe `ReserveStockAsync` does the
check and the subtraction inside `JsonFileStore.UpdateAsync`, while the file's
`SemaphoreSlim` is held, so the check and the update are one step. The safe
run gave one `True`, one `False`, and stock `0`.

## Phase 3: Full checkout workflow

### Results of the main run (all 20 orders, with the injected failures)

| Status | Count | Orders |
|---|---|---|
| Completed | 11 | #1, 2, 3, 6, 7, 11, 14, 16, 17, 19, 20 |
| Rejected | 6 | #4, 9, 10, 12, 15, 18 |
| PaymentFailed | 2 | #5 (`Payment gateway timeout (3s)`), #8 (`Card declined by issuer`) |
| Failed | 1 | #13 (`Database save failed after payment; payment TXN-14A6AE9BBC98 refunded`) |

All 20 orders ran at the same time and finished in 13.4 s. `orders.json` contains
exactly 20 records. Orders #10 and #11 both wanted the last Mechanical Keyboard:
#11 won and #10 was rejected with `Insufficient stock for 'Mechanical Keyboard'`.

Evidence from my log for the controlled failures:

- **Payment timeout (#5):** `Capturing payment for Order #5...` is followed by
  `Order #5 -> Step 4/6 Payment ... PaymentFailed: Payment gateway timeout (3s) after 3948 ms`.
  The gateway needs 5000 ms to capture #5, but the 3-second limit cancels it first,
  so no payment record is written for #5 and its stock is released.
- **Declined payment (#8):** `Payment declined for Order #8`, then
  `Step 4/6 Payment ... PaymentFailed: Card declined by issuer after 2252 ms`.
- **Refund after a failed save (#13):** `Commit failed for Order #13`, then the
  `Refund API` lines, `Refunded TXN-14A6AE9BBC98`, `Released 2 of Product #6`, and
  `Step 5/6 Save ... Failed: Database save failed after payment; payment TXN-14A6AE9BBC98 refunded after 2887 ms`.
  The order is then saved as `Failed`. That save succeeds because the injected failure
  only fires when the order is saved as `Completed`.
- **Email retry (#3 and #17):** #3 failed attempts 1 and 2 and succeeded on
  attempt 3 (`Step 6/6 Email ... OK in 3574 ms`). #17 failed all 4 attempts and was
  written to `email_retry.json` (`Email for Order #17 queued for retry later`,
  `Step 6/6 Email ... OK in 5577 ms`). Both orders stayed `Completed`, because a
  failed email never changes the order status.

### Sync vs async comparison (Task 9.6)

I ran orders #1 to #10 with all failures disabled. Both versions gave
8 Completed and 2 Rejected (#4 and #9), as expected.

| Version | Time | Result | Distinct threads that logged | Pool threads before -> after |
|---|---|---|---|---|
| Sync: 10 `Thread` objects, `Thread.Sleep` | 12.5 s | 8 Completed, 2 Rejected | 10 | 4 -> 4 |
| Async: 10 tasks, `Task.Delay` | 10.2 s | 8 Completed, 2 Rejected | 6 | 4 -> 7 |

("Distinct threads" counts only the threads that wrote a log line, and
`ThreadPool.ThreadCount` does not include the 10 dedicated threads of the sync run.)

**1. Does async make one order's workflow faster?**
Mostly no. Inside one order each step needs the result of the previous one
(customer, stock, pricing, payment, save, email), so the steps run one after the
other in both versions. The only overlap is the discount and shipping calls in
pricing. In Phase 2, pricing order #1 took 4.8 s sequentially and 3.5 s
concurrently, and that overlap is what I think explains the 2.3 s gap here
(12.5 s against 10.2 s). In the sync log, order #2's pricing runs its SubTotal,
Discount and Shipping calls one after another, while the async run overlaps the
last two. Async does not shorten a wait. It only stops the thread from sitting
idle during it.

**2. Why does async scale better for many orders?**
An `await` releases the thread while the order waits, so a few threads can serve
many orders. In my main run, 20 `CustomerRepository -> Opening connection...`
lines were all written by `Thread 9` within about 1 ms (22:35:33.920 to .921), so
one thread started 20 operations. In the comparison the async run used 6 threads
for 10 orders, and the sync run needed 10, one per order, each blocked in
`Thread.Sleep` during every delay.

**3. What happens to the sync version with 1,000 orders?**
It would need about 1,000 threads, nearly all of them blocked in `Thread.Sleep`
or waiting for a file lock. Each thread has its own stack and the operating
system has to schedule all of them, so memory use and switching cost grow with the
number of orders. I did not run 1,000 orders, so this is my reasoning and not a
measurement. The async version would keep using a small number of threads, and
its time would depend on the delays and on the lock around `products.json`, not
on a thread count. My main run already shows that lock: the stock step took about
0.9 s for the first orders (`Order #12 ... Step 2/6 Stock ... OK in 926 ms`) and
about 1.9 s for the later ones (`Order #20 ... in 1911 ms`), because all 20 orders
queue on the same file.

**4. Why is async essential for apps that call databases, files and APIs?**
These calls spend nearly all their time waiting for something outside the
program. If every waiting call holds a thread, the number of requests the app can
handle at once is limited by its threads, and the threads are wasted on idle
waiting. With async the thread is released during the wait and can serve other
requests. In my project every database, API, file and email step is a wait of
150 to 5000 ms, so a blocked thread would be idle for almost all of an order.

### Fire-and-forget investigation (Task 9.7)

I implemented the broken method exactly as given, with the CS4014 warning
suppressed (`#pragma warning disable CS4014`), and ran it with a working email
service and with a failing one. Then I ran the fixed version both ways.

**1. What is the problem?**
`_emailService.SendConfirmationAsync(order, customer)` is called without `await`.
The method starts the email and moves on, so `Order processing finished` is
printed before the email has been sent, and the method's `Task` completes while
the email is still running. The compiler warns about this (CS4014), and I had to
suppress the warning to keep the broken code.

**2. What happens if the email is still being sent when the method returns, or when the console app exits?**
Method returns first (case 1, working email):

```
[22:35:21.569] EmailService -> Rendering template for Order #1 (attempt 1)...
Order processing finished
Emails in emails.json right after the method returned: 0
[22:35:22.084] EmailService -> Sending email for Order #1...
[22:35:22.598] EmailService -> Email for Order #1 sent (attempt 1)
Emails in emails.json 1.5 s later: 1
```

`Order processing finished` is printed before the email's connect and send steps,
and `emails.json` has 0 records right after the method returned and 1 record
1.5 s later. Only the part of the email method before its first `await` ran on
the caller's thread, which is why the `Rendering template` line appears first.

App exits first (separate run where `Main` returns right after the broken method):

```
[10:13:07.199] [Thread 7] OrderRepository -> Order #1 saved
[10:13:07.202] [Thread 7] EmailService -> Rendering template for Order #1 (attempt 1)...
Order processing finished
Main is returning now: the console app exits.
```

The output stops there. The `Connecting to SMTP`, `Sending email` and
`Email for Order #1 sent` lines never appear, so the email was lost. Nothing
awaits it, so nothing keeps the process alive while it runs. The write to
`emails.json` comes after the send step, which never ran, so no email record was
written.

**3. What happens if the email throws an exception?**
Case 2 (failing email):

```
Order processing finished
[22:35:25.384] EmailService -> Connecting to SMTP for Order #1...
The method reported success and no error was raised to the caller.
Emails in emails.json: 0
[UnobservedTaskException] SMTP server unavailable
```

The exception is stored inside the task that nobody awaits, so the caller never
sees it: the method reports success, the order looks finished, and the customer
never gets an email. The only trace was the `[UnobservedTaskException]` line, and
it appeared only because my demo forced a garbage collection (`GC.Collect()`).
Without that, the failure would stay invisible.

**4. The fix, and the output before and after.**
I added `await` in front of the email call, and caught `SmtpUnavailableException`
so that an email failure is logged and does not fail the order.

Before (case 1): `Order processing finished` is printed before the email is sent,
and `emails.json` has 0 records at that point.

After (case 3, working email):

```
[22:35:29.372] EmailService -> Rendering template for Order #1 (attempt 1)...
[22:35:29.575] EmailService -> Connecting to SMTP for Order #1...
[22:35:29.876] EmailService -> Sending email for Order #1...
[22:35:30.391] EmailService -> Email for Order #1 sent (attempt 1)
Order processing finished
Emails in emails.json right after the method returned: 1
```

After (case 4, failing email):

```
[22:35:33.197] FireAndForgetDemo -> Email for Order #1 failed: SMTP server unavailable
Order processing finished
```

Now the email lines come before `Order processing finished`, `emails.json` has 1
record when the method returns, and a failing email shows up as a red error line
instead of vanishing. The real `OrderProcessor` applies the same rule in step 6:
the confirmation email is awaited through `SendConfirmationWithRetryAsync`.

**When is `await` necessary?**
Whenever the next line depends on the result, the caller needs to know whether
the work succeeded, or an exception has to reach the caller. In my project the
confirmation email belongs to the order's workflow: the order must not be reported
as finished before the email attempt is done, and a failure has to be handled
(the retry queue in `email_retry.json`).

**When is fire-and-forget acceptable, if ever?**
Only when nobody needs the result and a failure does no harm, for example
non-critical logging or telemetry. Even then the background work should catch and
log its own exceptions, and the program must not exit before it has finished. It
is not acceptable for an email the customer expects, because failures vanish
silently, as case 2 showed.

**Why is `async void` dangerous?**
An `async void` method returns no `Task`, so the caller cannot await it, cannot
know when it finishes, and cannot catch its exceptions. An exception thrown inside
it cannot be handled by the caller, and in a console app it can bring down the
whole process. It is also hard to test.

**Why is `async void` acceptable for event handlers?**
The signature of an event handler is fixed by the framework and returns `void`
(for example a button's `Click` handler), so an async handler has no other
choice. The handler is also the top of the call chain, so nothing above it could
await it anyway. It should catch its own exceptions inside.

### Performance table rows (Phase 3)

| Measurement | Time | Distinct threads that logged | Pool threads |
|---|---|---|---|
| Phase 3 sync, 10 `Thread`s, orders #1 to #10 | 12.5 s | 10 | 4 -> 4 |
| Phase 3 async, 10 tasks, orders #1 to #10 | 10.2 s | 6 | 4 -> 7 |
| Phase 3 async, all 20 orders with failures | 13.4 s | not measured | not measured |