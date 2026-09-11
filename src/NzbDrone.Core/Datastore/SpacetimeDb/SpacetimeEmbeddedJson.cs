using System.Text.Json;
using System.Text.Json.Serialization;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    // Mirrors NzbDrone.Core.Datastore.Converters.EmbeddedDocumentConverter<T>'s settings exactly
    // (camelCase property names, case-insensitive reads, custom TimeSpan/UTC/enum converters).
    // Needed whenever a ported entity reuses one of the real codebase's System.Text.Json
    // converters (e.g. CustomFormatIntConverter, QualityIntConverter) rather than a plain
    // self-consistent round trip via SpacetimeJson/Newtonsoft - those converters were written
    // against these exact options and rely on the camelCase discriminator/property names they
    // produce.
    public static class SpacetimeEmbeddedJson
    {
        public static JsonSerializerOptions Create(params JsonConverter[] extraConverters)
        {
            var options = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true));
            options.Converters.Add(new STJTimeSpanConverter());
            options.Converters.Add(new STJUtcConverter());

            foreach (var converter in extraConverters)
            {
                options.Converters.Add(converter);
            }

            return options;
        }
    }
}
