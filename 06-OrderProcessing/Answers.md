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