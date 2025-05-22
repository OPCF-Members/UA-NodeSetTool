using McMaster.Extensions.CommandLineUtils;
using NodeSetTool;

try
{
    NodeSetToolConsole.Run(args);

    //const string FileName = "Opc.Ua.NodeSet2.Services";
    //const string InputDirectory = @"D:\Work\OPC\nodesets\v105\Schema\";
    //const string OutputDirectory = @"D:\Work\OPC\UA-NodeSetTool2\examples\";

    //const string FileName = "DemoModel.NodeSet2";
    //const string InputDirectory = @"D:\Work\OPC\nodesets\v105\DemoModel\";
    //const string OutputDirectory = @"D:\Work\OPC\UA-NodeSetTool2\examples\";

    //NodeSetToolConsole.Run([
    //    "convert",
    //    "--in", $"{InputDirectory}{FileName}.xml",
    //    "--out", $"{OutputDirectory}{FileName}.json",
    //    "--type", "json"
    //]);

    //NodeSetToolConsole.Run([
    //    "compare",
    //    "--in", $"{InputDirectory}{FileName}.xml",
    //    "--target", $"{OutputDirectory}{FileName}.json",
    //]);


    //NodeSetToolConsole.Run([
    //    "convert",
    //    "--in", $"{InputDirectory}{FileName}.xml",
    //    "--out", $"{OutputDirectory}{FileName}.tar.gz",
    //    "--type", "tar.gz",
    //    "--max", "100"
    //]);

    //NodeSetToolConsole.Run([
    //    "compare",
    //    "--in", $"{InputDirectory}{FileName}.xml",
    //    "--target", $"{OutputDirectory}{FileName}.tar.gz",
    //]);

    //NodeSetToolConsole.Run([
    //    "convert",
    //    "--in", $"{InputDirectory}{FileName}.tar.gz",
    //    "--out", $"{OutputDirectory}{FileName}.xml",
    //    "--type", "xml"
    //]);

    //NodeSetToolConsole.Run([
    //    "compare",
    //    "--in", $"{InputDirectory}{FileName}.xml",
    //    "--target",  $"{OutputDirectory}{FileName}.xml",
    //]);
}
catch (CommandParsingException e)
{
    Log.Error($"[{e.GetType().Name}] {e.Message} ({e.Command})");
    Environment.Exit(3);
}
catch (AggregateException e)
{
    Log.Error($"[{e.GetType().Name}] {e.Message}");

    foreach (var ie in e.InnerExceptions)
    {
        Log.Warning($">>> [{ie.GetType().Name}] {ie.Message}");
    }

    Environment.Exit(3);
}
catch (Exception e)
{
    Log.Error($"[{e.GetType().Name}] {e.Message}");

    Exception ie = e.InnerException;

    while (ie != null)
    {
        Log.Warning($">>> [{ie.GetType().Name}] {ie.Message}");
        ie = ie.InnerException;
    }

    Log.Debug($"========================");
    Log.Debug($"{e.StackTrace}");
    Log.Debug($"========================");

    Environment.Exit(3);
}