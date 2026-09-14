using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.Qualities;
using Sonarr.Http;

namespace Sonarr.Api.V3.Profiles.Quality
{
    [V3ApiController("qualityprofile/schema")]
    public class QualityProfileSchemaController : Controller
    {
        private readonly IQualityProfileService _profileService;

        public QualityProfileSchemaController(IQualityProfileService profileService)
        {
            _profileService = profileService;
        }

        [HttpGet]
        public async Task<QualityProfileResource> GetSchema()
        {
            var qualityProfile = await _profileService.GetDefaultProfile(string.Empty);

            return qualityProfile.ToResource();
        }
    }
}
