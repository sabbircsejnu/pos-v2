using RetailPOS.API.Controllers;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Architecture;

public sealed class CleanArchitectureDependencyTests
{
    [Fact]
    public void Domain_HasNoProductionProjectDependencies()
    {
        var references = typeof(AuditEvent).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name?.StartsWith("RetailPOS.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Application_ReferencesDomainButNotInfrastructureApiOrEfCore()
    {
        var references = typeof(IAuditEventReadStore).Assembly.GetReferencedAssemblies();
        var names = references.Select(reference => reference.Name).ToHashSet();

        Assert.Contains("RetailPOS.Domain", names);
        Assert.DoesNotContain("RetailPOS.Infrastructure", names);
        Assert.DoesNotContain("RetailPOS.API", names);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", names);
    }

    [Fact]
    public void Infrastructure_ReferencesDomainAndApplicationButNotApi()
    {
        var references = typeof(RetailPOSDbContext).Assembly.GetReferencedAssemblies();
        var names = references.Select(reference => reference.Name).ToHashSet();

        Assert.Contains("RetailPOS.Domain", names);
        Assert.Contains("RetailPOS.Application", names);
        Assert.DoesNotContain("RetailPOS.API", names);
    }

    [Fact]
    public void ApiControllers_DoNotInjectRetailPosDbContext()
    {
        var controllers = typeof(AuditEventsController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract
                && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type));

        Assert.All(controllers, controller =>
            Assert.DoesNotContain(
                controller.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType),
                parameterType => parameterType == typeof(RetailPOSDbContext)));
    }

    [Fact]
    public void ApiControllers_DoNotExposeInfrastructurePersistenceOrEfCoreTypes()
    {
        var controllers = typeof(AuditEventsController).Assembly.GetTypes()
            .Where(type => !type.IsAbstract
                    && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(type));
        var referencedTypes = controllers.SelectMany(controller =>
            controller.GetConstructors()
                    .SelectMany(constructor => constructor.GetParameters())
                    .Select(parameter => parameter.ParameterType)
                    .Concat(controller.GetFields(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly)
                        .Select(field => field.FieldType))
                    .Concat(controller.GetProperties(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly)
                        .Select(property => property.PropertyType))
                    .Concat(controller.GetMethods(
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.DeclaredOnly)
                        .SelectMany(method => method.GetParameters()
                            .Select(parameter => parameter.ParameterType)
                            .Append(method.ReturnType))));

        Assert.DoesNotContain(
            referencedTypes.SelectMany(ExpandType),
            type => type.Assembly.GetName().Name is "Microsoft.EntityFrameworkCore"
                        or "Microsoft.EntityFrameworkCore.Abstractions"
                    || type.Namespace is { } namespaceName
                        && (namespaceName == "RetailPOS.Infrastructure.Data"
                            || namespaceName.StartsWith(
                                "RetailPOS.Infrastructure.Data.",
                                StringComparison.Ordinal)
                            || namespaceName == "RetailPOS.Infrastructure.Repositories"
                            || namespaceName.StartsWith("RetailPOS.Infrastructure.Repositories.", StringComparison.Ordinal)));
    }

    [Fact]
    public void ApiServicesAndControllers_DoNotReferencePersistenceInSourceOrMemberTypes()
    {
        var api = typeof(AuditEventsController).Assembly;
        var types = api.GetTypes().Where(type => type.Namespace is { } name
            && (name.StartsWith("RetailPOS.API.Services", StringComparison.Ordinal)
                || name.StartsWith("RetailPOS.API.Controllers", StringComparison.Ordinal)
                || name.StartsWith("RetailPOS.API.Documents", StringComparison.Ordinal)));
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly;
        Assert.All(types, type =>
        {
            var members = type.GetConstructors(flags).SelectMany(member => member.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(type.GetFields(flags).Select(field => field.FieldType))
                .Concat(type.GetMethods(flags).SelectMany(method =>
                    method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
                .Concat(type.GetInterfaces());
            Assert.DoesNotContain(members.SelectMany(ExpandType), IsPersistenceType);
        });

        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API"));
        // Program.cs is the composition root; Infrastructure Audit adapters, host and PDF presentation are allowed.
        foreach (var folder in new[] { "Controllers", "Services", "Documents" })
        {
            var files = Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            Assert.All(files, file => Assert.False(ContainsPersistenceDependency(File.ReadAllText(file)),
                $"API persistence dependency in {Path.GetRelativePath(root, file)}"));
        }
    }

    [Theory]
    [InlineData("using RetailPOS.Infrastructure.Repositories;")]
    [InlineData("using Db = RetailPOS.Infrastructure.Data.RetailPOSDbContext;")]
    [InlineData("Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(query);")]
    [InlineData("private RetailPOSDbContext db;")]
    public void ApiPersistenceSourceGuard_RecognizesForbiddenDependencies(string source) =>
        Assert.True(ContainsPersistenceDependency(source));

    private static bool ContainsPersistenceDependency(string source) =>
        source.Contains("RetailPOS.Infrastructure.Repositories", StringComparison.Ordinal)
        || source.Contains("RetailPOS.Infrastructure.Data", StringComparison.Ordinal)
        || source.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        || source.Contains("RetailPOSDbContext", StringComparison.Ordinal);

    private static bool IsPersistenceType(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("RetailPOS.Infrastructure.Data", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("RetailPOS.Infrastructure.Repositories", StringComparison.Ordinal) == true;

    private static IEnumerable<Type> ExpandType(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nestedType in ExpandType(elementType))
                yield return nestedType;
        }

        yield return type;

        if (!type.IsGenericType)
            yield break;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nestedType in ExpandType(argument))
                    yield return nestedType;
        }
    }
}
