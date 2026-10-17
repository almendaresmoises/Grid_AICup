using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Grid;

/// <summary>
/// Catches OperationCanceledException when the client aborts an HTTP connection (e.g. page refresh, navigation).
/// Runs before AbpExceptionFilter so normal client disconnects are not logged as internal server errors.
/// </summary>
public class ClientCancellationExceptionFilter : IAsyncExceptionFilter, IOrderedFilter
{
    public int Order => -1000;

    private readonly ILogger<ClientCancellationExceptionFilter> _logger;

    public ClientCancellationExceptionFilter(ILogger<ClientCancellationExceptionFilter> logger)
    {
        _logger = logger;
    }

    public Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is OperationCanceledException &&
            context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogInformation(
                "HTTP {Method} {Path} was canceled by the client.",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path);

            context.ExceptionHandled = true;
            context.Result = new StatusCodeResult(499);
        }

        return Task.CompletedTask;
    }
}
