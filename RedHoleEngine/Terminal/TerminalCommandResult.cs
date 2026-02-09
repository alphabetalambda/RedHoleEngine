namespace RedHoleEngine.Terminal;

public sealed class TerminalCommandResult
{
    public TerminalCommandResult(string output, int exitCode = 0)
    {
        Output = output;
        ExitCode = exitCode;
    }

    public string Output { get; }
    public int ExitCode { get; }
}
