using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Persistence;
using Nexus.Modules.FileStorage.Application;
using Nexus.Modules.FileStorage.Infrastructure;

namespace Nexus.Modules.FileStorage;

public static class FileStorageModuleExtensions
{
    public static IServiceCollection AddFileStorageModule(this IServiceCollection services, string connectionString)
    {
        services.AddNexusModuleDbContext<FileStorageDbContext>(connectionString, "file_storage");
        services.AddScoped<IFileStorageService, FileStorageService>();
        return services;
    }
}
