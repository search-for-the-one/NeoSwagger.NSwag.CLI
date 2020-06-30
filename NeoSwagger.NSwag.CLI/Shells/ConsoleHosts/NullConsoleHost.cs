namespace NeoSwagger.NSwag.CLI.Shells.ConsoleHosts
{
    public class NullConsoleHost : IConsoleHost
    {
        public int PrintTextMaxChars { get; } = int.MaxValue;

        public void Write(string text)
        {
        }

        public void WriteLine()
        {
        }

        public void WriteLine(string line)
        {
        }

        public string ReadLine(string message) => string.Empty;
    }
}