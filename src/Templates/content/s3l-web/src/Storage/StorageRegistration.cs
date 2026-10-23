using Snail.Toolkit.Mongodb.DependencyInjection;

namespace Snail.Web.Template.Storage;

/// <summary>Where the application keeps its data: MongoDB.</summary>
internal static class StorageRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>The client and the database the Mongodb section names, checked at start.</summary>
        public IServiceCollection AddStorage(IConfiguration configuration) => services.AddMongodb(configuration);
    }
}
