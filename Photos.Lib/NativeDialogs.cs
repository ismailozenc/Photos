using System;
using System.Collections.Concurrent;

namespace Photos.Lib
{
    /// <summary>
    /// Lets the host platform show a native dialog in place of the drawn one.
    /// Return true from <see cref="Show"/> when the dialog was shown; the chosen button's action is then
    /// queued with <see cref="Post"/> and runs on the render thread through <see cref="RunPending"/>.
    /// </summary>
    public static class NativeDialogs
    {
        public static Func<string[], MessageBoxButton[], bool> Show;

        /// <summary>Asks for a password. The callback runs later on the render thread, with the text or null when cancelled.</summary>
        public static Action<string, Action<string>> PasswordPrompt;

        static readonly ConcurrentQueue<Action> _pending = new ConcurrentQueue<Action>();

        /// <summary>Queues work that a dialog thread produced, for the render thread.</summary>
        public static void Post(Action action) => _pending.Enqueue(action);

        /// <summary>Runs queued dialog results. Call on the render thread once per frame.</summary>
        public static void RunPending()
        {
            while (_pending.TryDequeue(out var action))
                action();
        }
    }
}
