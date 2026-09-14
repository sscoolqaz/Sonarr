using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.RootFolders
{
    [V3ApiController]
    public class RootFolderController : RestControllerWithSignalR<RootFolderResource, RootFolder>
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderController(IRootFolderService rootFolderService,
                                IBroadcastSignalRMessage signalRBroadcaster,
                                RootFolderValidator rootFolderValidator,
                                PathExistsValidator pathExistsValidator,
                                MappedNetworkDriveValidator mappedNetworkDriveValidator,
                                RecycleBinValidator recycleBinValidator,
                                StartupFolderValidator startupFolderValidator,
                                SystemFolderValidator systemFolderValidator,
                                FolderWritableValidator folderWritableValidator)
        : base(signalRBroadcaster)
        {
            _rootFolderService = rootFolderService;

            SharedValidator.RuleFor(c => c.Path)
                .Cascade(CascadeMode.Stop)
                .IsValidPath()
                           .SetValidator(rootFolderValidator)
                           .SetValidator(mappedNetworkDriveValidator)
                           .SetValidator(startupFolderValidator)
                           .SetValidator(recycleBinValidator)
                           .SetValidator(pathExistsValidator)
                           .SetValidator(systemFolderValidator)
                           .SetValidator(folderWritableValidator);
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // ProviderControllerBase.cs for the full rationale).
        protected override RootFolderResource GetResourceById(int id)
        {
            var timeout = Request?.GetBooleanQueryParameter("timeout", true) ?? true;

            return _rootFolderService.Get(id, timeout).GetAwaiter().GetResult().ToResource();
        }

        [RestPostById]
        [Consumes("application/json")]
        public async Task<ActionResult<RootFolderResource>> CreateRootFolder([FromBody] RootFolderResource rootFolderResource)
        {
            var model = rootFolderResource.ToModel();

            return Created((await _rootFolderService.Add(model)).Id);
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<RootFolderResource>> GetRootFolders()
        {
            return (await _rootFolderService.AllWithUnmappedFolders()).ToResource();
        }

        [RestDeleteById]
        public async Task DeleteFolder(int id)
        {
            await _rootFolderService.Remove(id);
        }
    }
}
