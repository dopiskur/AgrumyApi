using api.Dal;
using api.Dal.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace api.Filters
{
    /// <summary>
    /// Turns any exception escaping an API action into a response: a named unique-constraint hit
    /// (email/username) becomes a 500 business message; anything else goes through
    /// <see cref="ISystemRepository.ClassifyException"/> to the DbErrorResponse shape with the status
    /// <see cref="DbErrorResponse.StatusCodeFor"/> picks (409 for a constraint violation, 500 for an
    /// unrecognised error, else 503). Registered globally in Program.cs. Takes the narrow
    /// ISystemRepository facet (roadmap #74) - classification is the only data-layer touchpoint here.
    /// </summary>
    public sealed class DbExceptionFilter(ISystemRepository repo, ILogger<DbExceptionFilter> logger) : IExceptionFilter
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
                DbFailureKind kind = repo.ClassifyException(ex);
                context.Result = new ObjectResult(DbErrorResponse.For(kind))
                {
                    StatusCode = DbErrorResponse.StatusCodeFor(kind),
                };
            }

            context.ExceptionHandled = true;
        }
    }
}
