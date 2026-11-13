namespace CDTag.Common
{
    /// <summary>The match timeout for CD-Tag's compile-time [GeneratedRegex] patterns.</summary>
    /// <remarks>Same limit as the process-wide default set in App (REGEX_DEFAULT_MATCH_TIMEOUT), stated on each
    /// generated regex so it holds wherever the pattern runs (tests, tools, other hosts).</remarks>
    public static class RegexTimeouts
    {
        /// <summary>5 seconds: far above any real file name, tag, cue sheet or log, low enough not to hang the UI.</summary>
        public const int Milliseconds = 5000;
    }
}
