namespace NeoSwagger.NSwag.CLI.Compilers
{
    internal interface ICompiler
    {
        byte[] Compile(string code);
    }
}