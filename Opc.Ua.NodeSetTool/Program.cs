using McMaster.Extensions.CommandLineUtils;
using NodeSetTool;

try
{
   //NodeSetToolConsole.Run(args);

    NodeSetToolConsole.Run([
        "convert",
        "--in", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.xml",
        "--type", "tar.gz",
        "--max", "100"
    ]);

    NodeSetToolConsole.Run([
        "compare",
        "--in", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.xml",
        "--target", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.tar.gz"
    ]);

    NodeSetToolConsole.Run([
        "convert",
        "--in", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.tar.gz",
        "--out", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.copy.xml",
        "--type", "xml"
    ]);

    NodeSetToolConsole.Run([
        "compare",
        "--in", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.xml",
        "--target", @"D:\Work\OPC\nodesets\v105\Schema\Opc.Ua.NodeSet2.Services.copy.xml"
    ]);
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