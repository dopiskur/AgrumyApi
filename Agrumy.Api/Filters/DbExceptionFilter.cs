using api.Dal;
using api.Dal.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace api.Filters
{
    /// <summary>
    /// Turns any exception escaping an API action into the response the controllers used to build
    /// by hand: a unique-constraint hit becomes a 500 with a business message, anything else goes
    /// through <see cref="IRepository.ClassifyException"/> to a 503 with the DbErrorResponse shape.
    /// Registered globally in Program.cs.
    /// </summary>
    public sealed class DbExceptionFilter(IRepository repo, ILogger<DbExceptionFilter> logger) : IExceptionFilter
    {
        public void OnException(ExceptionContext context)
        {
            var ex = context.Exception;
            if (ex is OperationCanceledException)
            {
                return; // client disconnected - let the framework handle it
            }

            if (DbErrorResponse.Mentions(ex, "email_UNIQUE"))
            {
                context.Result = new ObjectResult("email already registered") { StatusCode = 500 };
            }
            else if (DbErrorResponse.Mentions(ex, "Username_UNIQUE"))
            {
                context.Result = new ObjectResult("username already registered") { StatusCode = 500 };
            }
            else
            {
                logger.LogError(ex, "API action {Action} failed", context.ActionDescriptor.DisplayName);
                context.Result = new ObjectResult(DbErrorResponse.For(repo.ClassifyException(ex)))
                {
                    StatusCode = StatusCodes.Status503ServiceUnavailable,
                };
            }

            context.ExceptionHandled = true;
        }
    }
}
