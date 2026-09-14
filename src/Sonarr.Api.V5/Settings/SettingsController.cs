using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Settings
{
    public abstract class SettingsController<TResource> : RestController<TResource>
        where TResource : RestResource, new()
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigService _configService;

        protected SettingsController(IConfigFileProvider configFileProvider, IConfigService configService)
        {
            _configFileProvider = configFileProvider;
            _configService = configService;
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
        // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
        // base class.
        protected override TResource GetResourceById(int id)
        {
            var resource = ToResource(_configFileProvider, _configService).GetAwaiter().GetResult();
            resource.Id = id;

            return resource;
        }

        private async Task<TResource> GetResourceByIdAsync(int id)
        {
            var resource = await ToResource(_configFileProvider, _configService);
            resource.Id = id;

            return resource;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<Ok<TResource>> GetConfig()
        {
            return TypedResults.Ok(await GetResourceByIdAsync(1));
        }

        [RestPutById]
        [Consumes("application/json")]
        [Produces("application/json")]
        public virtual Task<Results<Accepted<TResource>, NotFound>> SaveSettings([FromBody] TResource resource)
        {
            var dictionary = resource.GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            return Task.FromResult(TypedAccepted(resource.Id));
        }

        protected abstract Task<TResource> ToResource(IConfigFileProvider configFile, IConfigService model);
    }
}
