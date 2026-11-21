using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nexus.Api.Controllers;
using Nexus.Modules.AccessControl;
using Nexus.Modules.Audit;
using Nexus.Modules.Configuration;
using Nexus.Modules.FileStorage;
using Nexus.Modules.Identity;
using Nexus.Modules.Integration;
using Nexus.Modules.Notification;
using Nexus.Modules.Organization;
using Nexus.Modules.Subscription;
using Nexus.Modules.Tenancy;
using Nexus.Modules.Workflow;
using Nexus.SharedKernel.Primitives;
using Xunit;

namespace Nexus.ArchitectureTests;

public sealed class ModuleBoundaryArchitectureTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(TenancyModuleExtensions).Assembly,
        typeof(OrganizationModuleExtensions).Assembly,
        typeof(IdentityModuleExtensions).Assembly,
        typeof(AccessControlModuleExtensions).Assembly,
        typeof(AuditModuleExtensions).Assembly,
        typeof(WorkflowModuleExtensions).Assembly,
        typeof(SubscriptionModuleExtensions).Assembly,
        typeof(ConfigurationModuleExtensions).Assembly,
        typeof(IntegrationModuleExtensions).Assembly,
        typeof(NotificationModuleExtensions).Assembly,
        typeof(FileStorageModuleExtensions).Assembly
    ];

    [Fact]
    public void SharedKernel_HasZeroFrameworkOrInfrastructureDependencies()
    {
        var refs = typeof(Entity).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("Npgsql", StringComparison.Ordinal));
        Assert.DoesNotContain(refs, r => r.StartsWith("RabbitMQ", StringComparison.Ordinal));
    }

    [Fact]
    public void Modules_DoNotReferenceEachOtherDirectly()
    {
        var moduleNames = ModuleAssemblies.Select(a => a.GetName().Name!).ToHashSet(StringComparer.Ordinal);

        foreach (var moduleAssembly in ModuleAssemblies)
        {
            var selfName = moduleAssembly.GetName().Name!;
            var referencedModules = moduleAssembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Where(name => moduleNames.Contains(name) && name != selfName)
                .ToList();

            Assert.Empty(referencedModules);
        }
    }

    [Fact]
    public void ApiControllers_NeverInjectDbContextDirectly()
    {
        var apiAssembly = typeof(TenantsController).Assembly;
        var controllers = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        Assert.NotEmpty(controllers);

        foreach (var controller in controllers)
        {
            var ctors = controller.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var ctor in ctors)
            {
                foreach (var param in ctor.GetParameters())
                {
                    Assert.False(
                        typeof(DbContext).IsAssignableFrom(param.ParameterType),
                        $"Controller {controller.Name} must not depend on DbContext ({param.ParameterType.Name}).");
                }
            }
        }
    }

    [Fact]
    public void ApiControllers_UseV1PrefixAndExplicitAuthorization()
    {
        var apiAssembly = typeof(TenantsController).Assembly;
        var controllers = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        foreach (var controller in controllers)
        {
            var route = controller.GetCustomAttribute<RouteAttribute>();
            Assert.NotNull(route);
            Assert.StartsWith("api/v1/", route.Template, StringComparison.Ordinal);

            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute))
                .ToList();

            foreach (var action in actions)
            {
                var hasAuth = action.GetCustomAttributes(true).Any(a => a is IAuthorizeData or IAllowAnonymous)
                              || controller.GetCustomAttributes(true).Any(a => a is IAuthorizeData);
                Assert.True(hasAuth, $"Action {controller.Name}.{action.Name} must declare explicit authorization or AllowAnonymous.");
            }
        }
    }

    [Fact]
    public void AuditController_ExposesZeroMutationEndpoints()
    {
        var methods = typeof(AuditController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            Assert.Null(method.GetCustomAttribute<HttpPostAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPutAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpPatchAttribute>());
            Assert.Null(method.GetCustomAttribute<HttpDeleteAttribute>());
        }
    }
}
