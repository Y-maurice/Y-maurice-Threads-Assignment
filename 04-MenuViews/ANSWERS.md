# Assignment 04 — Answers

- Student name:  Youssef Maurice
- Group / ID: Sunday & Wednesday

## Why Expected and Actual disagree

Eight threads each call `RegisterView()` 50,000 times, so the counter should
reach 400,000. My run before the fix printed:

```
Expected: 400000
Actual:   199952
```

The problem is that `Views++` looks like one step, but it is really three:
read the current value, add 1, write the result back. Nothing stops two
threads from interleaving these steps. For example, if two threads both read
`100`, both add 1 and both write `101`, then two views happened but only one
was counted. One update is lost.

With 8 threads running a tight loop with no delay, this collision happens all
the time, so about half of the increments were lost in my run. `Actual` can
never be higher than `Expected`, because updates can only be lost, never
created. The number also changes from run to run, because it depends on how the
threads happen to be scheduled.

## What you changed

I changed `RegisterView` to use an atomic operation:

```csharp
public void RegisterView()
{
    Interlocked.Increment(ref Views);
}
```

`Interlocked.Increment` does the read, add and write as one indivisible
operation, so no other thread can get in the middle of it and no update is
lost. After the fix, every run printed:

```
Expected: 400000
Actual:   400000
```

I chose `Interlocked` over `lock` because this job is a single integer counter.
A `lock` would also give the correct result, but it makes threads take a key,
wait for each other, and release it, which is more than this job needs. An
atomic operation is a single step, so it is the simpler and cheaper tool for
one counter.

`Interlocked.Increment` needs a `ref` to the variable. This works because
`Views` is a field. It would not compile with a property, because a property
is a method call and has no memory location to pass by reference.

## When would you protect the update differently

`Interlocked` is only enough when the update is one operation on one variable.
I would use something else when:

- **Two or more values must change together.** For example, in Part 02
  `TablesLeft` and `Booked` had to stay consistent. Two separate atomic
  operations could still let another thread see one changed and not the other.
  I would use a `lock` around the whole update.
- **I must check a condition and then act on it.** For example, "book a table
  only if tables are left". The check and the action must happen as one unit,
  otherwise another thread can change the value in between. Here I would use a
  `lock` around both.
- **The update is a bigger piece of work,** such as changing a list or a
  dictionary, or doing several steps that depend on each other. A single atomic
  operation can't cover that, so I would protect the whole section with a
  `lock` (or a thread-safe collection).
