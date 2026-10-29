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