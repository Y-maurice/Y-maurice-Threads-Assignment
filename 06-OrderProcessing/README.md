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



## Phase 4: Scale-up

From Phase 4 on, the "start the tasks, then await them one by one" rule no longer
applies. Each task has a requirement that rule cannot meet, so each one uses a different tool.

| Task | Tool I used | Why "start, then await one by one" is not enough |
|---|---|---|
| 10.1 Live checkout progress | `Task.WhenEach` | Lines must appear in finishing order, not start order |
| 10.2 Customer dashboard | `Task.WhenAll`, one `await` | One `await` for all loads, and every failure must be reported |
| 10.3 Fastest shipping quote | `Task.WhenAny` in a loop + a linked `CancellationTokenSource` | The first successful quote wins and the losers are cancelled |
| 10.4 A Fraud scoring (CPU-bound) | `Parallel.ForEach` with `MaxDegreeOfParallelism = Environment.ProcessorCount` | CPU work needs several threads running at the same instant |
| 10.4 B Email re-send (I/O-bound) | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4` | At most 4 sends at the same time, enforced by the library |

### Results of my run

**10.1 Live checkout progress.** All 20 orders were started at once. A line was printed
the moment each order finished, in finishing order: `[ 1/20] Order #4 -> Rejected
(Customer is inactive) after 1.5 s` came first and `[20/20] Order #17 -> Completed after
13.4 s` came last. Orders #5, #6 and #16 all finished at 9.3 s. After the last line the
counts were Rejected 6, PaymentFailed 2, Completed 11, Failed 1, which matches Task 9.4.

**10.2 Customer dashboard.** The four loads (profile, orders, payments, emails) started
together and were awaited with a single `await`.

- Customer #3: `Omar Hassan — 2 orders (#3 Completed, #13 Failed), 2 payments (#13 Refunded,
  #3 Captured), 1 emails (#3)`, loaded in 0.8 s.
- Customer #99 (payments load set to fail): `2 failures after 0.8 s`: `Payments store
  timeout` and `Customer #99 not found`.

Each load alone takes 0.5 to 0.8 s (200 ms plus 300 to 600 ms), so awaiting them one after
another would take about 2 to 3 s. The 0.8 s I measured is close to the slowest single load.

**10.3 Fastest shipping quote.** Budget 1.2 s, flat rate 25.00 when no quote arrives in time.

| Order | Result | Time | Carriers cancelled |
|---|---|---|---|
| #1 | Flat rate 25.00 | 1214 ms | all three (budget over) |
| #2 | FastShip 7.30 | 908 ms | NileCourier (CairoExpress failed: even order id) |
| #3 | NileCourier 11.77 | 1091 ms | FastShip, CairoExpress |
| #4 | FastShip 7.10 | 970 ms | NileCourier (CairoExpress failed: even order id) |
| #5 | Flat rate 25.00 | 1213 ms | all three (budget over) |

The Flat-rate results happen when every carrier's random wait is long: a carrier needs
150 ms for the handshake plus its own random time, so a slow draw passes 1.2 s. No order
took longer than about 1.2 s. Separate runs:

- Order #6 with every carrier configured to fail: `All carriers failed`
  (`ShippingProviderUnavailableException`).
- Order #1 with a 300 ms budget: `Flat rate, price 25.00` after 301 ms.
- Order #1 with the caller's token cancelled after 200 ms: every carrier logged
  `[Carrier] cancelled`.

**10.4 A Fraud scoring (CPU-bound).** 10,000 orders scored.

- Flagged: 1005 (first five: 4, 8, 12, 29, 33). The sequential and the parallel run
  returned the same ids.
- Sequential: 12.53 s. Parallel: 2.04 s (speed-up x6.1) with `Environment.ProcessorCount` = 8.
- The parallel run used 9 distinct threads over its whole length. This is not a contradiction
  with the limit: `MaxDegreeOfParallelism = Environment.ProcessorCount` limits how many
  iterations run at the same time to 8, but over the run the thread pool can use a different
  thread for a later piece of work. (I checked the meaning of this option in the
  `ParallelOptions.MaxDegreeOfParallelism` documentation on Microsoft Learn.)

**10.4 B Email re-send (I/O-bound).** 30 emails (orders 1001 to 1030), no injected failures.

- Highest number of sends running at the same time: exactly 4 (measured by a counter that only
  observes, it does not limit).
- `emails.json` (in the `Data-resend` folder) contains 30 records for 30 distinct orders.
- Total time 8.2 s. Each send takes about 1 s (200 + 300 + 500 ms), and 30 sends in groups
  of 4 is about 8 rounds, so about 8 s.

### Performance table rows (Phase 4)

| Measurement | Time | Distinct threads that logged | ThreadPool.ThreadCount |
|---|---|---|---|
| 10.1 All 20 orders, live progress | 13.4 s | not measured | not measured |
| 10.2 Customer dashboard #3, 4 loads at once | 0.8 s | not measured | not measured |
| 10.3 Fastest shipping quote, orders #1 to #5 | 908 to 1214 ms each (budget 1.2 s) | not measured | not measured |
| 10.4 A Fraud scoring, sequential | 12.53 s | 1 | not measured |
| 10.4 A Fraud scoring, parallel | 2.04 s (speed-up x6.1) | 9 (8 cores) | not measured |
| 10.4 B Email re-send, 30 emails, max 4 at once | 8.2 s | not measured | not measured |

### Answers to the Phase 4 questions

**1. For each of Tasks 10.1 to 10.4: which tool did you use, and why can't "start, then await one by one" meet the requirement?**

- **10.1:** `Task.WhenEach`. Awaiting the tasks in the order they were started prints
  the lines in start order, and one slow order (for example #17 with its email
  retries, 13.4 s, or #5 with its 3-second timeout) would hold back the lines of every
  order after it, even if they finished long ago. `Task.WhenEach` hands out each task the
  moment it completes, so in my run the lines came out as #4 (1.5 s), #15, #9, #11, ...
  #17 (13.4 s). It also does not need a list that I search or rebuild after every
  completion (requirement R4).
- **10.2:** `Task.WhenAll` with a single `await`. The four loads start together and
  the total time is close to the slowest load (0.8 s), not the sum. Awaiting them one by one
  would stop at the first failed load, and the others would never be looked at, but
  R4 asks for every failure.
- **10.3:** `Task.WhenAny` in a loop. Awaiting the carriers in list order would wait for
  the first carrier even when another one answered much sooner, and a failing carrier
  would throw before a working one was even looked at. `WhenAny` tells me which carrier
  finishes first, so I can take the first successful quote and keep waiting if the
  finished one failed (for order #2, CairoExpress failed and FastShip's quote won).
- **10.4 A:** `Parallel.ForEach`. The work is CPU-bound: a thread that is running the
  loop is busy, so awaiting does not make it faster. Only several threads running at
  the same instant, on different cores, shorten the time (12.53 s down to 2.04 s).
- **10.4 B:** `Parallel.ForEachAsync`. Starting all 30 sends and awaiting them one by one
  would run all 30 at the same time, but R1 says at most 4. The library enforces the
  limit with `MaxDegreeOfParallelism = 4`, so I did not write a counter or a semaphore
  to limit it.

**2. In 10.2, when several loads fail, how do you get every exception? What does a plain `await` give you?**
A plain `await` on a faulted task rethrows only the first exception, even when
several of the combined tasks failed. `Task.WhenAll` returns one combined task whose
`Exception` property is an `AggregateException` holding the failure of every load. So I
keep the combined task in a variable, `await` it inside a `try`, and in the `catch` I read
`all.Exception.InnerExceptions` and throw them all together. For customer #99 this gave
two failures: `Payments store timeout` and `Customer #99 not found`.

**3. In 10.3, what happens to the losing carriers if you never cancel them?**
They keep running in the background after the winner is chosen. They finish their
handshake and their random wait for nothing, so they hold connections and use time,
and in a real system they would cost money or API calls for an answer nobody uses. If
one of them fails, its exception is stored in a task nobody awaits and is lost. I link
every carrier to one `CancellationTokenSource`, together with the 1.2 s budget and the
caller's token. When a quote is chosen, I cancel the source. In my run, when FastShip won
order #2 at 908 ms, `[NileCourier] cancelled` was logged at once, instead of NileCourier
running until its own random wait ended.

**4. Why does 10.4 A get faster with more CPU cores, while 10.4 B does not depend on the number of cores?**
10.4 A is CPU-bound: every score is calculation, so each core that runs a
thread computes its own part of the 10,000 orders at the same time. More cores means
more parts run at once, up to `Environment.ProcessorCount`. On my 8 cores it took 12.53 s
sequentially and 2.04 s in parallel (x6.1, less than x8 because of the cost of starting
and coordinating the threads). 10.4 B is I/O-bound: each send is mostly waiting (200 +
300 + 500 ms of simulated SMTP work) and uses almost no CPU. Its time is decided by the
limit of 4 at a time: 30 emails in groups of 4 is about 8 rounds of about 1 s, so
the 8.2 s I measured would be the same on any number of cores.

**5. What is the difference between parallel work (10.4 A) and concurrent asynchronous work (10.4 B)?**
Parallel work means several pieces of work are executing at the same instant on
different cores. It needs several threads that are all busy, and it is for CPU-bound
work. Concurrent asynchronous work means many operations are in progress at the same
time, but most of them are waiting, so very few threads are needed: a thread is
released at each `await` and used again when the wait ends. In 10.4 B up to 4 sends
were in progress at once (my counter showed a highest value of 4), and the log shows
that a handful of threads handled all 30. They overlap their waiting, and nothing needs a
core while they wait.

**6. Show a cancelled run (`--cancel-after`) and explain what happened to the orders, the stock and the payments.**
I ran the checkout workflow with `--cancel-after 4000` and `--cancel-after 7000`.

```
dotnet run --project OrderProcessing -- phase3 --cancel-after 4000
Run cancelled — 2 orders finished, 18 cancelled

dotnet run --project OrderProcessing -- phase3 --cancel-after 7000
Run cancelled — 4 orders finished, 16 cancelled
```

- **Orders:** the finished orders kept their result (at 4000 ms, #4 and #15 were
  already `Rejected`). Every other order was saved as `Pending` with
  `FailureReason = "Cancelled"`. None became `Failed` or `PaymentFailed`. No new order
  was started after the cancel, and `orders.json` still held 20 valid records in both runs.
- **Payments:** at 7000 ms order #11 had already captured its payment, so the undo
  refunded it. `payments.json` contains one record for order 11, `TXN-B0DC1BF0127F`, with
  status `Refunded`, and no record is left as `Captured` for a cancelled order.
- **Stock:** an order that had reserved stock releases every reserved item before it is
  saved as `Pending`, so a cancelled run does not leave stock reserved.
- **Total time was longer than the cancel time** (6.0 s for a 4 s cancel, 9.5 s for
  7 s), because every order undoes its work and the stock releases queue on the same
  `products.json` lock.
- "Orders finished" counts every order that reached a final status before the cancel,
  rejected ones included.

### Cancellation design (spec 6.6)

- Every async method takes a `CancellationToken` as its last parameter and passes it to
  every `Task.Delay`, file call and repository call.
- `Program` creates one `CancellationTokenSource` per phase run. Ctrl+C sets
  `e.Cancel = true`, so the process is not killed, and cancels the source.
  `--cancel-after <ms>` calls `CancelAfter`.
- No order is started once the token is cancelled. An order in progress stops at its next
  `await`, and its processor catches the cancellation and undoes what that order did:
  it refunds a captured payment, releases reserved stock, and saves the order as
  `Pending` with `Cancelled`.
- The undo runs with its own token that has a 30-second limit, because the run's token
  is already cancelled and would stop the undo at once. This is the one place where a
  method uses a different token than the one it received, and I did it on purpose.
- `ProductRepository.ReserveStockAsync` catches a cancellation that arrives during its
  final commit wait. At that point the stock is already written to `products.json`, so
  it returns its real result and lets the caller undo the reservation. Otherwise the
  stock would stay reserved forever.
- If the cancel arrives during the confirmation email, the order stays `Completed`,
  because it is already paid and saved.
- `JsonFileStore` writes a temporary file and then moves it over the real one, so a
  cancelled or crashed write never leaves a half-written JSON file.
- The demos (sync vs async, fire-and-forget) have their own run mode, `phase3-demos`,
  so `phase3 --cancel-after` cancels the real workflow and not a demo.

## Problems I found while building the project and how I solved them

1. **Two orders reserved the last keyboard.** The unsafe version read the stock, waited,
   and then wrote it, so both orders read `1` and both got it (stock stayed `0`, so
   nothing looked wrong). The fix is `JsonFileStore.UpdateAsync`, which does the read,
   the check and the write while holding the file's lock.
2. **A `lock` cannot contain an `await`.** I used one `SemaphoreSlim` per file with
   `WaitAsync(ct)`, released in a `finally`.
3. **A crash during a write could leave a half-written file.** Writes go to a `.tmp`
   file first and then replace the real file with `File.Move(..., overwrite: true)`.
4. **Tasks that are never awaited hide their exceptions.** In Phase 2 order #7's shipping
   failure was logged at once but only handled when the runner reached it, so its stock
   stayed reserved for about 4 s. This is a real limit of awaiting in order, and it
   is also why 10.1 uses `Task.WhenEach`.
5. **A cancelled run could leak stock or payments.** An order cancelled after its payment
   was captured needed a refund, and its undo had to run with its own token (see the
   cancellation design above).
6. **Async looked slower than sync in Phase 1** (53.5 s against 35.3 s). The async
   stopwatch also covers the 20 saves of about 0.9 s each, and the sync run saves nothing,
   as the task says. Validation alone takes about the same time in both runs.
7. **Console colors could mix between threads.** `Log` prints each line inside a `lock`
   that covers setting the color, writing, and resetting.
8. **A shipping carrier that lost the race could look like a failure.** I separated three
   outcomes of a carrier task: a quote (the winner), a failure (the next carrier is
   awaited), and a cancellation (the carrier left the race because it lost or the
   budget ran out).