# Assignment 03 — Answers

- Student name: Youssef Maurice 
- Group / ID: Sunday & wednesday

1. `System.Threading.Monitor` is used to protect a critical section so that only
   one thread at a time can run it (mutual exclusion). A thread calls
   `Monitor.Enter(x)` to take the lock on an object `x`. If another thread
   already holds it, the calling thread blocks until the owner releases it.
   When the critical section ends, the thread calls `Monitor.Exit(x)` so a
   waiting thread can proceed. The object `x` works as the key, so every thread
   must lock on the same object, which is why it is usually kept in a
   `private readonly` field that only the class can see. Different objects
   mean different keys, and the threads would not block each other. `Exit` has
   to run even when an exception is thrown, so it is placed in a `finally`
   block, or the lock would never be released and the other threads would wait
   forever. Besides locking, `Monitor` also lets threads wait for a condition
   and signal each other (`Wait` and `Pulse`).

2. The `lock` statement is shorthand for the `Monitor` pattern. For a normal
   object, `lock (x) { ... }` compiles to a `try` / `finally` block. Inside the
   `try`, the compiler calls `Monitor.Enter(x, ref lockTaken)` and then runs the
   critical section. In the `finally`, it calls `Monitor.Exit(x)` if
   `lockTaken` is true. The `finally` is there so the lock is released even if
   an exception is thrown inside the block. Without it, the key would never be
   returned and the other threads would wait forever. The `lockTaken` flag makes
   sure `Exit` is only called when the lock was really acquired. When the object
   is a `System.Threading.Lock` (.NET 9), the compiler doesn't use
   `Monitor`. It calls `EnterScope()` on the lock inside a `using`, and
   disposing the returned scope releases the lock.

3. `lock` and calling `Monitor` directly both give mutual exclusion, but they
   differ in these ways:
   - **Safety and length.** `lock` is shorthand: the compiler writes the `try` /
     `finally` and the `Exit` call for me, so I can't forget to release the
     lock. With `Monitor.Enter` / `Monitor.Exit` I have to write the
     `try` / `finally` myself. If I forget the `finally` and an exception is
     thrown, the lock is never released and the other threads wait forever.
   - **Waiting behavior.** `lock` always waits as long as it takes to get the
     key. `Monitor` has `TryEnter`, which can return immediately or give up
     after a timeout, so a thread doesn't have to block forever.
   - **Extra features.** `lock` only protects a block of code. `Monitor` also
     has `Wait`, `Pulse` and `PulseAll`, which let threads wait for a
     condition and signal each other. `lock` has no syntax for this.
   - **Scope.** A `lock` has to start and end in one block. With `Monitor` the
     `Enter` and `Exit` calls can be in different places in the code, but the
     same thread must call both, and this makes mistakes easier.

4. A developer uses these `Monitor` methods when a plain `lock` is not enough.

   - **`Monitor.TryEnter`:** I would use it when waiting forever for the lock
     is a bad idea. `TryEnter` tries to take the lock and returns `true` or
     `false`, and it can also be given a timeout. If the lock is busy, the thread
     can skip the work, try again later, or report an error instead of
     blocking. Example: a background job that refreshes a cache every minute.
     If another thread is already refreshing it, the job can skip this round.
     Another case is code that must stay responsive, like a UI thread, or code
     that wants to avoid a deadlock by giving up after a timeout.

   - **`Monitor.Wait` and `Monitor.Pulse`:** I would use them when a thread has
     to wait for a condition and another thread tells it when the condition may
     have changed. Example: a producer-consumer queue. The consumer locks the
     queue, and if the queue is empty it calls `Monitor.Wait`. `Wait` releases
     the lock and puts the thread to sleep, so the producer can enter and add an
     item. After adding it, the producer calls `Monitor.Pulse`, which wakes a
     waiting consumer. The consumer takes the lock again and continues. This is
     better than a loop that keeps checking the queue, because the waiting
     thread uses no CPU. `Wait` and `Pulse` must be called while holding the
     lock, and the condition should be rechecked in a `while` loop after waking
     up, because the state may have changed again.

5. The same critical section, written twice. Both versions use the same
   `private readonly object _key`, and both do the same thing: check the
   tables, wait, decrement, and increment as one unit.

   **Version 1: with `lock`**

   ```csharp
   private readonly object _key = new object();

   public void Book(string customerName)
   {
       lock (_key)
       {
           if (TablesLeft <= 0)
               return;

           Thread.Sleep(20);

           TablesLeft--;
           Booked++;
           Console.WriteLine($"{customerName} booked a table. Left={TablesLeft}");
       }
   }
   ```

   **Version 2: with `Monitor.Enter` / `Monitor.Exit`**

   ```csharp
   private readonly object _key = new object();

   public void Book(string customerName)
   {
       bool lockTaken = false;
       try
       {
           Monitor.Enter(_key, ref lockTaken);

           if (TablesLeft <= 0)
               return;

           Thread.Sleep(20);

           TablesLeft--;
           Booked++;
           Console.WriteLine($"{customerName} booked a table. Left={TablesLeft}");
       }
       finally
       {
           if (lockTaken)
               Monitor.Exit(_key);
       }
   }
   ```

   The second version is what the compiler generates for the first one. The
   `return` inside the `try` still releases the lock, because the `finally`
   block runs before the method returns.

Links:

- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
- https://learn.microsoft.com/en-US/dotnet/fundamentals/runtime-libraries/system-threading-monitor
