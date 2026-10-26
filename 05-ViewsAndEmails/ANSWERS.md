# Assignment 05 — Answers

- Student name: Youssef Maurice 
- Group / ID: Sunday & Wednesday


## Failures you observed


I ran the original project 2 times. The results changed from run to run:

```
Views expected=50000, actual=19540
Email jobs placed=20, unique sent=20, total sends=20

Views expected=50000, actual=31066
Email jobs placed=20, unique sent=19, total sends=19
```

**Views.** The count was always far below 50000, and it was different every
time. `Dictionary` is not thread-safe, and `RecordView` does several separate
steps: `ContainsKey`, then `Add`, then read the value, add 1 and write it back.
Ten threads run these steps at once, so two threads can read the same value and
both write the same result. One increment is lost.

**Emails.** Some runs were correct and some were not. In one run 20 jobs were
placed but only 19 were sent, with unique and total both 19, so one job
disappeared without any error. `Queue<T>` is not thread-safe, so 20 producers
calling `Enqueue` together can corrupt it. The worker also does
`if (Count > 0) Dequeue()`. With two workers, both can see `Count > 0` for the
same last job, and one of them then fails. This is a check-then-act gap, and it
is the reason the original code has a `catch (InvalidOperationException)`.


## What you changed

- `Views` is now a `ConcurrentDictionary<string, int>`, and `RecordView` is one
  call: `Views.AddOrUpdate(product, 1, (_, current) => current + 1)`. If the key
  is missing it adds 1, otherwise it increments the current value, all as one
  atomic operation. The separate check, add and update steps are gone.
- `EmailJobs` is now a `ConcurrentQueue<string>`, so producers can enqueue at the
  same time without overwriting each other.
- The worker uses `TryDequeue` instead of `if (Count > 0) Dequeue()`. It checks
  and removes in one atomic step, and returns false if the queue is empty, so two
  workers can't both take the same last job. I removed the
  `catch (InvalidOperationException)`, because the exception no longer happens.
- `SentEmails` is still a `List<string>`, which is not thread-safe, so I kept
  the `lock` around `Add`.
- In `EmailWorker` I read `ProducersDone` before calling `TryDequeue`. If the
  flag was already true, all jobs were already enqueued, so a failed
  `TryDequeue` really means the queue is empty and the worker can exit. Checking
  the flag after a failed dequeue could leave a job unsent.

After the fix, I ran the program many times and every run printed:

```
Views expected=50000, actual=50000
Email jobs placed=20, unique sent=20, total sends=20
```

## Collection safety vs business-rule safety

A concurrent collection makes each single call safe: one `Enqueue`, one
`TryDequeue` or one indexer write can't corrupt the collection. But a business
rule is usually a sequence of calls, and another thread can run between the
calls. The rule here is "every view is counted exactly once, and every email
is sent exactly once".

I tested this by changing only the types and keeping the old logic:

- With a `ConcurrentDictionary`, the old code `ContainsKey`, `Add`, then
  `Views[product] = Views[product] + 1` still reads the value, adds 1 and writes
  it back as separate steps. Each call is safe, but the sequence is not, so two
  threads can still read the same number and lose an update.
- With a `ConcurrentQueue`, a check of `Count` followed by a dequeue still has a
  gap between the check and the act. Another worker can take the job in between.

The rule only stays safe when the check and the update happen as one atomic
operation, which is what `AddOrUpdate` and `TryDequeue` do. So choosing a
thread-safe collection is not enough. I also have to use it in a way that makes
the whole step of the rule atomic.