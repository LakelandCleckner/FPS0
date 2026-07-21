namespace Combat.Status
{
    // Diagnostic logging for the status system, compiled out by default.
    //
    // [Conditional] removes the ENTIRE call at every call site, including evaluation
    // of the arguments — so the interpolated strings the [StackPool] logs used to
    // build every single tick simply cease to exist unless you opt in.
    //
    // To turn it on: Project Settings > Player > Other Settings > Scripting Define
    // Symbols, add STATUS_DEBUG. (Or #define STATUS_DEBUG at the top of a file.)
    internal static class StatusLog
    {
        [System.Diagnostics.Conditional("STATUS_DEBUG")]
        public static void Log(string message) => UnityEngine.Debug.Log(message);

        [System.Diagnostics.Conditional("STATUS_DEBUG")]
        public static void Warn(string message) => UnityEngine.Debug.LogWarning(message);
    }
}
