# Assignment 02 — Answers

- Student name:  Youssef Maurice
- Group / ID: Sunday & Wednesday

## What is wrong

My broken runs gave wrong totals, and the numbers changed between runs:

```
Booked=7,  TablesLeft=-1
Booked=22, TablesLeft=-18
```

There are only 5 tables, so `Booked` should never be above 5 and
`TablesLeft` should never be negative. There are two bugs:

1. **Check-then-act race.** `Book` checks `TablesLeft <= 0`, then waits
   (`Thread.Sleep(20)`), then decrements. Between the check and the decrement
   there is a gap. With 40 threads starting together, many of them pass the
   check while `TablesLeft` is still positive, then they all decrement after
   their sleep. This is why more than 5 customers got a table. The sleep
   doesn't cause the bug, it only makes the gap big enough to hit every time.

2. **Lost updates.** `TablesLeft--` and `Booked++` are not single steps. Each
   one reads the value, changes it, and writes it back. If two threads read the
   same value, one write overwrites the other. I can prove this from my runs,
   because `Booked + TablesLeft` should always be 5 if no update is lost.

## How you fixed it

I added a private `Lock` field in `FridayNightBooking` and wrapped the whole
body of `Book` in `lock (_lock) { ... }`. Only one thread at a time can be
inside that block, so:

- The check, the decrement and the increment happen as one unit. No other
  thread can pass the check between my check and my decrement, which fixes
  bug 1.
- Only one thread changes `TablesLeft` and `Booked` at a time, so no update
  is lost, which fixes bug 2.
- The `Console.WriteLine` is also inside the lock, so the `Left` value it
  prints is consistent.

The check had to go inside the lock too. If only the decrement were locked,
the gap between the check and the lock would still exist.



output of the runs after fix:

```
Customer-01 booked a table. Left=4
Customer-03 booked a table. Left=3
Customer-02 booked a table. Left=2
Customer-04 booked a table. Left=1
Customer-05 booked a table. Left=0

Booked=5, TablesLeft=0
```
