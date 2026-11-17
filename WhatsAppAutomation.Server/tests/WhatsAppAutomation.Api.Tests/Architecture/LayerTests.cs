using NetArchTest.Rules;

namespace WhatsAppAutomation.Api.Tests.Architecture;

/// <summary>
/// Clean Architecture rules. Project references already stop Domain from using Application and so
/// on; these tests also stop the inner layers from using web or database frameworks directly.
/// Dependency direction: Server → Infrastructure → Application → Domain.
/// </summary>
public sealed class LayerTests
{
    private const string Domain = "WhatsAppAutomation.Domain";
    private const string Application = "WhatsAppAutomation.Application";
    private const string Infrastructure = "WhatsAppAutomation.Infrastructure";
    private const string Server = "WhatsAppAutomation.Api";

    [Fact]
    public void Domain_depends_on_no_other_layer_or_framework()
    {
        var result = Types.InAssembly(typeof(WhatsAppAutomation.Domain.Entities.RefreshToken).Assembly).ShouldNot()
            .HaveDependencyOnAny(Application, Infrastructure, Server,
                "MediatR", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_web_or_database_driver()
    {
        var result = Types.InAssembly(typeof(WhatsAppAutomation.Application.DependencyInjection).Assembly).ShouldNot()
            .HaveDependencyOnAny(Infrastructure, Server, "Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_web_host()
    {
        var result = Types.InAssembly(typeof(WhatsAppAutomation.Infrastructure.DependencyInjection).Assembly).ShouldNot()
            .HaveDependencyOn(Server)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Controllers_send_requests_through_MediatR_and_never_touch_the_database()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().HaveNameEndingWith("Controller")
            .ShouldNot().HaveDependencyOnAny(Infrastructure, "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Handlers_are_internal_so_only_MediatR_calls_them()
    {
        var result = Types.InAssembly(typeof(WhatsAppAutomation.Application.DependencyInjection).Assembly)
            .That().HaveNameEndingWith("Handler")
            .Should().NotBePublic()
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Breaking types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
