﻿using System;

namespace NeoSwagger.NSwag.CLI.Shells.ConsoleHosts
{
    public class SystemConsoleHost : IConsoleHost
    {
        private readonly LineEditor lineEditor = new(string.Empty);
        private int printTextMaxChars = 1200;

        public int PrintTextMaxChars
        {
            get => printTextMaxChars;
            set => printTextMaxChars = value < 0 ? int.MaxValue : value;
        }

        public void Write(string text) => Console.Write(text);
        public void WriteLine() => Console.WriteLine();
        public void WriteLine(string line) => Console.WriteLine(line);
        public string ReadLine(string message) => lineEditor.Edit(message, string.Empty);
    }
}