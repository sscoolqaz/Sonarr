using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Network;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;
using Sonarr.Http.Validation;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/host")]
    public class HostConfigController : RestController<HostConfigResource>
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigService _configService;
        private readonly IUserService _userService;

        public HostConfigController(IConfigFileProvider configFileProvider,
                                    IConfigService configService,
                                    IUserService userService,
                                    IDiskProvider diskProvider)
        {
            _configFileProvider = configFileProvider;
            _configService = configService;
            _userService = userService;

            SharedValidator.RuleFor(c => c.BindAddress)
                           .ValidIpAddress()
                           .When(c => c.BindAddress != "*" && c.BindAddress != "localhost");

            SharedValidator.RuleFor(c => c.Port).ValidPort();

            SharedValidator.RuleFor(c => c.AllowedHosts).NotNull();

            SharedValidator.RuleFor(c => c.AllowedHosts)
                           .Must(h => AllowedHostsParser.Parse(h).Any())
                           .When(c => c.AuthenticationRequired != AuthenticationRequiredType.Enabled)
                           .WithMessage("Allowed Hosts is required when 'Authentication Required' is not 'Enabled'");

            SharedValidator.RuleFor(c => c.AllowedHosts)
                           .ValidHosts()
                           .When(c => c.AllowedHosts.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c.UrlBase).ValidUrlBase();
            SharedValidator.RuleFor(c => c.TrustedNetworks).ValidIpNetworks();
            SharedValidator.RuleFor(c => c.InstanceName).StartsOrEndsWithSonarr();

            SharedValidator.RuleFor(c => c.Username).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Forms);
            SharedValidator.RuleFor(c => c.Password).NotEmpty().When(c => c.AuthenticationMethod == AuthenticationType.Forms);

            SharedValidator.RuleFor(c => c.AuthenticationMethod)
#pragma warning disable CS0618 // Type or member is obsolete
                .NotEqual(AuthenticationType.Basic)
#pragma warning restore CS0618 // Type or member is obsolete
                .WithMessage("'Basic' is no longer supported, switch to 'Forms' instead.");

            SharedValidator.RuleFor(c => c.PasswordConfirmation)
                .Must((resource, p) => IsMatchingPassword(resource)).WithMessage("Must match Password");

            SharedValidator.RuleFor(c => c.SslPort).ValidPort().When(c => c.EnableSsl);
            SharedValidator.RuleFor(c => c.SslPort).NotEqual(c => c.Port).When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.SslCertPath)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath()
                .SetValidator(new FileExistsValidator(diskProvider))
                .IsValidCertificate()
                .When(c => c.EnableSsl);

            SharedValidator.RuleFor(c => c.SslKeyPath)
                .NotEmpty()
                .IsValidPath()
                .SetValidator(new FileExistsValidator(diskProvider))
                .When(c => c.SslKeyPath.IsNotNullOrWhiteSpace());

            SharedValidator.RuleFor(c => c.LogSizeLimit).InclusiveBetween(1, 10);

            SharedValidator.RuleFor(c => c.Branch).NotEmpty().WithMessage("Branch name is required, 'main' is the default");
            SharedValidator.RuleFor(c => c.UpdateScriptPath).IsValidPath().When(c => c.UpdateMechanism == UpdateMechanism.Script);

            SharedValidator.RuleFor(c => c.BackupFolder).IsValidPath().When(c => Path.IsPathRooted(c.BackupFolder));
            SharedValidator.RuleFor(c => c.BackupInterval).InclusiveBetween(1, 7);
            SharedValidator.RuleFor(c => c.BackupRetention).InclusiveBetween(1, 90);
        }

        // NOTE: FluentValidation's synchronous `Must()` predicate can't await; the request
        // validation pipeline (RestController.ValidateResource) is itself synchronous
        // framework code out of scope for this pass, so we bridge here as the documented
        // boundary (see ProviderControllerBase.cs) rather than converting FluentValidation's
        // Must to MustAsync app-wide.
        private bool IsMatchingPassword(HostConfigResource resource)
        {
            var user = _userService.FindUser().GetAwaiter().GetResult();

            if (user != null && user.Password == resource.Password)
            {
                return true;
            }

            if (resource.Password == resource.PasswordConfirmation)
            {
                return true;
            }

            return false;
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override HostConfigResource GetResourceById(int id)
        {
            return GetHostConfig().GetAwaiter().GetResult();
        }

        [HttpGet]
        public async Task<HostConfigResource> GetHostConfig()
        {
            var resource = _configFileProvider.ToResource(_configService);
            resource.Id = 1;

            var user = await _userService.FindUser();

            resource.Username = user?.Username ?? string.Empty;
            resource.Password = user?.Password ?? string.Empty;
            resource.PasswordConfirmation = string.Empty;

            return resource;
        }

        [RestPutById]
        public async Task<ActionResult<HostConfigResource>> SaveHostConfig([FromBody] HostConfigResource resource)
        {
            resource.TrustedNetworks = IPNetworkParser.NormalizeList(resource.TrustedNetworks);

            var dictionary = resource.GetType()
                                     .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                                     .ToDictionary(prop => prop.Name, prop => prop.GetValue(resource, null));

            _configFileProvider.SaveConfigDictionary(dictionary);
            _configService.SaveConfigDictionary(dictionary);

            if (resource.Username.IsNotNullOrWhiteSpace() && resource.Password.IsNotNullOrWhiteSpace())
            {
                await _userService.Upsert(resource.Username, resource.Password);
            }

            return Accepted(resource.Id);
        }
    }
}
