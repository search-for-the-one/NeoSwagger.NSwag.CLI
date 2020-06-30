namespace NeoSwagger.NSwag.CLI.Shells
{
    public interface IConsoleHost
    {
        int PrintTextMaxChars { get; }
        
        void Write(string text);
        void WriteLine();
        void WriteLine(string line);
        string ReadLine(string message);
    }
}