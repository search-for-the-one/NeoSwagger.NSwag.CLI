using NeoSwagger.NSwag.CLI;

namespace CLI.App
{
    public static class Program
    {
        public static int Main(string[] args) => new CommandLineInterface().Run(args);
    }
}