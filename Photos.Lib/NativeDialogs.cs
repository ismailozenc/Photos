using System;

namespace Photos.Lib
{
    /// <summary>
    /// Lets the host platform show a native dialog in place of the drawn one.
    /// Return true when the dialog was shown; the chosen button's action is then invoked
    /// by the host. Return false (or leave <see cref="Show"/> null) to use the drawn dialog.
    /// </summary>
    public static class NativeDialogs
    {
        public static Func<string[], MessageBoxButton[], bool> Show;
    }
}
