using System.Text.Json;
using api.Dal;
using api.Dal.Interface;
using api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Agrumy.Api.Tests;

/// <summary>
/// The global filter that replaced the per-action try/catch blocks: unique-constraint hits become
/// a 500 business message, everything else goes through IRepository.ClassifyException -> 503.
/// </summary>
public class DbExceptionFilterTests
{
    private static ExceptionContext Context(Exception ex)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        return new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = ex };
    }

    private static DbExceptionFilter Filter(DbFailureKind kind = DbFailureKind.ConnectionFailure)
    {
        var repo = new Mock<IRepository>();
        repo.Setup(r => r.ClassifyException(It.IsAny<Exception>())).Returns(kind);
        return new DbExceptionFilter(repo.Object, NullLogger<DbExceptionFilter>.Instance);
    }

    [Fact]
    public void ConnectionFailure_Becomes503WithDbErrorResponse()
    {
        var ctx = Context(new InvalidOperationException("db down"));
        Filter(DbFailureKind.ConnectionFailure).OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
        Assert.Contains("connection_failure", JsonSerializer.Serialize(obj.Value));
        Assert.True(ctx.ExceptionHandled);
    }

    [Fact]
    public void SchemaMissing_Becomes503()
    {
        var ctx = Context(new Exception("Table 'x' doesn't exist"));
        Filter(DbFailureKind.SchemaMissing).OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
        Assert.Contains("schema_missing", JsonSerializer.Serialize(obj.Value));
    }

    [Fact]
    public void ConstraintViolation_Becomes409WithDbErrorResponse()
    {
        var ctx = Context(new Exception("FK violation"));
        Filter(DbFailureKind.ConstraintViolation).OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status409Conflict, obj.StatusCode);
        Assert.Contains("constraint_violation", JsonSerializer.Serialize(obj.Value));
        Assert.True(ctx.ExceptionHandled);
    }

    [Fact]
    public void Contention_Becomes503WithDbErrorResponse()
    {
        var ctx = Context(new Exception("deadlock found"));
        Filter(DbFailureKind.Contention).OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, obj.StatusCode);
        Assert.Contains("contention", JsonSerializer.Serialize(obj.Value));
    }

    [Fact]
    public void UnknownError_Becomes500NotAMisleading503()
    {
        var ctx = Context(new ArgumentException("Wrong id, no such person"));
        Filter(DbFailureKind.Unknown).OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, obj.StatusCode);
        Assert.Contains("server_error", JsonSerializer.Serialize(obj.Value));
        Assert.DoesNotContain("connection_failure", JsonSerializer.Serialize(obj.Value));
    }

    [Fact]
    public void UniqueEmailViolation_Becomes500BusinessMessage()
    {
        var ctx = Context(new Exception("outer",
            new Exception("Duplicate entry 'a@b.com' for key 'user.email_UNIQUE'")));
        Filter().OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(500, obj.StatusCode);
        Assert.Equal("email already registered", obj.Value);
        Assert.True(ctx.ExceptionHandled);
    }

    [Fact]
    public void UniqueUsernameViolation_Becomes500BusinessMessage()
    {
        var ctx = Context(new Exception("Duplicate entry 'bob' for key 'Username_UNIQUE'"));
        Filter().OnException(ctx);

        var obj = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(500, obj.StatusCode);
        Assert.Equal("username already registered", obj.Value);
    }

    [Fact]
    public void OperationCanceled_IsLeftUnhandled()
    {
        var ctx = Context(new OperationCanceledException());
        Filter().OnException(ctx);

        Assert.Null(ctx.Result);
        Assert.False(ctx.ExceptionHandled);
    }
}
