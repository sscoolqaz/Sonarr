using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Update.History;
using Sonarr.Http;

namespace Sonarr.Api.V3.Update
{
    [V3ApiController]
    public class UpdateController : Controller
    {
        private readonly IRecentUpdateProvider _recentUpdateProvider;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly IConfigFileProvider _configFileProvider;

        public UpdateController(IRecentUpdateProvider recentUpdateProvider, IUpdateHistoryService updateHistoryService, IConfigFileProvider configFileProvider)
        {
            _recentUpdateProvider = recentUpdateProvider;
            _updateHistoryService = updateHistoryService;
            _configFileProvider = configFileProvider;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<UpdateResource>> GetRecentUpdates()
        {
            var resources = (await _recentUpdateProvider.GetRecentUpdatePackages())
                                                 .OrderByDescending(u => u.Version)
                                                 .ToResource();

            if (resources.Any())
            {
                var first = resources.First();
                first.Latest = true;

                if (first.Version > BuildInfo.Version)
                {
                    first.Installable = true;
                }

                var installed = resources.SingleOrDefault(r => r.Version == BuildInfo.Version);

                if (installed != null)
                {
                    installed.Installed = true;
                }

                if (!_configFileProvider.LogDbEnabled)
                {
                    return resources;
                }

                var updateHistory = await _updateHistoryService.InstalledSince(resources.Last().ReleaseDate);
                var installDates = updateHistory
                                                        .DistinctBy(v => v.Version)
                                                        .ToDictionary(v => v.Version);

                foreach (var resource in resources)
                {
                    if (installDates.TryGetValue(resource.Version, out var installDate))
                    {
                        resource.InstalledOn = installDate.Date;
                    }
                }
            }

            return resources;
        }
    }
}
