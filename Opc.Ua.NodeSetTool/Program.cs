using Microsoft.Extensions.Logging;
using NodeSetTool;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
});

var logger = loggerFactory.CreateLogger("NodeSetTool");

try
{
    NodeSetToolConsole.Run(args, logger);
}
catch (AggregateException e)
{
    logger.LogError("[{ExceptionType}] {Message}", e.GetType().Name, e.Message);

    foreach (var ie in e.InnerExceptions)
    {
        logger.LogWarning(">>> [{ExceptionType}] {Message}", ie.GetType().Name, ie.Message);
    }

    Environment.Exit(3);
}
catch (Exception e)
{
    logger.LogError("[{ExceptionType}] {Message}", e.GetType().Name, e.Message);

    Exception ie = e.InnerException;

    while (ie != null)
    {
        logger.LogWarning(">>> [{ExceptionType}] {Message}", ie.GetType().Name, ie.Message);
        ie = ie.InnerException;
    }

    logger.LogTrace("========================");
    logger.LogTrace("{StackTrace}", e.StackTrace);
    logger.LogTrace("========================");

    Environment.Exit(3);
}
