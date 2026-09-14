using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NzbDrone.Core.AutoTagging.Specifications;

namespace NzbDrone.Core.Datastore.Converters
{
    public class AutoTaggingSpecificationConverter : JsonConverter<List<IAutoTaggingSpecification>>
    {
        public override void Write(Utf8JsonWriter writer, List<IAutoTaggingSpecification> value, JsonSerializerOptions options)
        {
            var wrapped = value.Select(x => new SpecificationWrapper
            {
                Type = x.GetType().Name,
                Body = x
            });

            JsonSerializer.Serialize(writer, wrapped, options);
        }

        public override List<IAutoTaggingSpecification> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ValidateToken(reader, JsonTokenType.StartArray);

            var results = new List<IAutoTaggingSpecification>();

            reader.Read();

            while (reader.TokenType == JsonTokenType.StartObject)
            {
                reader.Read();
                ValidateToken(reader, JsonTokenType.PropertyName);

                reader.Read();
                ValidateToken(reader, JsonTokenType.String);
                var typename = reader.GetString();

                reader.Read();
                ValidateToken(reader, JsonTokenType.PropertyName);

                reader.Read();
                ValidateToken(reader, JsonTokenType.StartObject);

                var type = Type.GetType($"NzbDrone.Core.AutoTagging.Specifications.{typename}, Sonarr.Core", true);
                var item = (IAutoTaggingSpecification)JsonSerializer.Deserialize(ref reader, type, options);
                results.Add(item);

                reader.Read();
                reader.Read();
            }

            ValidateToken(reader, JsonTokenType.EndArray);

            return results;
        }

        private void ValidateToken(Utf8JsonReader reader, JsonTokenType tokenType)
        {
            if (reader.TokenType != tokenType)
            {
                throw new JsonException($"Invalid token: Was expecting a '{tokenType}' token but received a '{reader.TokenType}' token");
            }
        }

        private class SpecificationWrapper
        {
            public string Type { get; set; }
            public object Body { get; set; }
        }
    }
}
