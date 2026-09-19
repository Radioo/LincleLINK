# CLAUDE.md

@AGENTS.md

## The UI never freezes, everything that takes time shows progress, and every view begins loading

These three rules outrank convenience, test simplicity and "it is only a small call".
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

### 3. Every view begins in its loading state

- Anything that shows loaded data starts as "loading" from the moment its view model
  exists, not from the moment the load is called. The loading flag defaults to `true`.
- An empty state ("Your library is empty", "This entry has no files") is a result. It
  may only show after a load has finished and found nothing. Never bind it to
  "the collection is empty" alone: that is also true before the first load, and the
  empty state then flashes at startup for as long as the first query takes.
- Four states, never mixed up: loading, loaded with items, loaded and empty, failed
  (with the reason and a way to retry). A filter that matches nothing is a fifth, and
  is not "empty".
- Tell the states after a list has been rebuilt, never while: a rebuild clears the list
  first, and a state that follows the collection reports "empty" for that moment.
- Counts and figures start as a placeholder ("Loading...", "..."), never as "0" or a
  blank. A picker says why it has nothing to pick.
- Refreshing data that is already on screen keeps it there until the new data is in.
- The main window itself starts in a boot state while the app is still starting.

### How to check before calling UI work done

- For each `await` reachable from a command or an event handler, name the thread the
  awaited work runs on. If the answer is "the caller's", fix it.
- For each user action, say what the user sees during the first 100 ms, and during
  second 5 on an entry with 150k files.
- For each view that shows loaded data, say what is on screen before the load was even
  started, while it runs, and after it found nothing. Cover it twice: a view model test
  (`LoadingStateTests`) and a headless test of the visible text through the real
  bindings (`LoadingStateViewTests`), which is what catches a wrong `IsVisible` binding.
- In a view, only the main content scrolls (the file tree, a list). Never wrap a
  header, a form or a toolbar section in a scroll container or cap its height to make
  room: shrink it instead (drop explanations once they have done their job, one-line
  rows that wrap). `ProgressLayoutTests` fails when anything but the file tree scrolls
  or when text outside it is cut off.
- A control that acts on the main content (a filter switch like "Show only changes")
  sits directly next to that content, with a label that says what it does.
- View model tests can't see layout. New or changed XAML gets a headless layout test
  in `LincleLINK.App.Views.Tests` (see `ProgressLayoutTests`): lay the view out with the
  real theme in the state that shows the new controls, at the normal size and at the
  main window's minimum, and assert that no text overlaps a bar or another text and
  that the main content keeps a usable size. This is a rectangle check, not a
  screenshot. Then still ask the user to look at it.
- Cover it with a test where a test can: a view model test that the busy flag and
  status are set while the awaited work is pending, and that the work did not run on
  the calling thread.
