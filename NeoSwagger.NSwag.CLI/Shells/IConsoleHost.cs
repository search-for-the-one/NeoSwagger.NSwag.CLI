namespace NeoSwagger.NSwag.CLI.Shells
{
    public interface IConsoleHost
    {
        void Write(string text);
        void WriteLine();
        void WriteLine(string line);
        string ReadLine(string message);
    }
}