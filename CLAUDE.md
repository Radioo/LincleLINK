# CLAUDE.md

@AGENTS.md

## The UI never freezes, and everything that takes time shows progress

These two rules outrank convenience, test simplicity and "it is only a small call".
Treat a violation as a bug of the same weight as data loss.

### 1. Nothing slow runs on the UI thread

- Slow means: any database call, any file or directory access, hashing, walking or
  building anything sized by an entry's file count (an entry can hold 150k files),
  and game detection.
- `await` does not move work off the UI thread. A method is only safe to await from a
  view model if it hops to the thread pool itself (`Task.Run`) or is real async I/O.
- **SQLite is synchronous underneath.** Microsoft.Data.Sqlite and EF Core's `...Async`
  methods on it run on the calling thread. `IInstanceRepository` is registered behind
  `BackgroundInstanceRepository`, which moves every call to the thread pool. Keep it
  that way, and never resolve or construct the SQLite repository directly in the app.
- Application services (`LincleLINK.Core/Application`) do their lookups, planning and
  disk checks inside `Task.Run`, not before it. Only dialogs come back to the caller's
  context, because a dialog opens a window.
- View models compute on the thread pool and only assign results on the UI thread.
  Collections bound to the UI are replaced with one range change, never row by row.
- No `.Result`, `.Wait()`, `GetAwaiter().GetResult()` or `Thread.Sleep` on the UI thread.

### 2. Everything that takes time has a progress indicator

- Every operation a user starts shows, from the first moment, that it is running: a
  progress bar with a percentage when the amount of work is known, an indeterminate
  bar with a status line saying what is happening when it is not.
- This includes short-looking steps that scale with entry size: loading an entry's
  files, duplicating an entry, recomputing a preview, scanning a dropped folder,
  hashing, detecting the game version, saving an entry.
- A phase with no measurable progress (one big database save, say) switches the bar to
  indeterminate with a status line. A bar stuck at 0% or 100% is not an indicator.
- Buttons that would start conflicting work are disabled while it runs, and long work
  can be cancelled unless cancelling would leave data half written.

### How to check before calling UI work done

- For each `await` reachable from a command or an event handler, name the thread the
  awaited work runs on. If the answer is "the caller's", fix it.
- For each user action, say what the user sees during the first 100 ms, and during
  second 5 on an entry with 150k files.
- Cover it with a test where a test can: a view model test that the busy flag and
  status are set while the awaited work is pending, and that the work did not run on
  the calling thread.
