using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.Workflow.Application;
using Nexus.Modules.Workflow.Infrastructure;

namespace Nexus.Modules.Workflow;

public static class WorkflowModuleExtensions
{
    public static IServiceCollection AddWorkflowModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<WorkflowDbContext>(connectionString, "workflow");
        services.AddScoped<IWorkflowService, WorkflowService>();
        return services;
    }
}
