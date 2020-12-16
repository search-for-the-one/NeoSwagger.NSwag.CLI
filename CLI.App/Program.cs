using System;
using NeoSwagger.NSwag.CLI;

namespace CLI.App
{
    public static class Program
    {
        public static int Main(string[] args) => new CommandLineInterface().Run(args, $"NeoSwagger.NSwag.CLI v{GetVersion()}");
        
        private static Version GetVersion() => typeof(CommandLineInterface).Assembly.GetName().Version;
    }
}