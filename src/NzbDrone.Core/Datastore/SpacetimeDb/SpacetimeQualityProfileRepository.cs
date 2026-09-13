using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using StdbQualityProfile = SpacetimeDB.Types.QualityProfile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeQualityProfileRepository : SpacetimeBasicRepository<QualityProfile, StdbQualityProfile>, IQualityProfileRepository
    {
        // FormatItems is stored as {format:<CustomFormat.Id>, score} pairs, not the full
        // CustomFormat object - mirrors CustomFormatIntConverter's storage shape exactly (the
        // real repository's Query() override rehydrates the full object from
        // ICustomFormatService.All() below, and skips formats that were since removed).
        private struct FormatItemDto
        {
            public int Format { get; set; }
            public int Score { get; set; }
        }

        private static readonly JsonSerializerOptions SerializerSettings = SpacetimeEmbeddedJson.Create();

        private readonly ICustomFormatService _customFormatService;

        public SpacetimeQualityProfileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator, ICustomFormatService customFormatService)
            : base(connection, eventAggregator)
        {
            _customFormatService = customFormatService;
        }

        protected override StdbQualityProfile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.QualityProfile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override QualityProfile ToModel(StdbQualityProfile row)
        {
            var cfs = _customFormatService.All().ToDictionary(c => c.Id);
            var dtos = JsonSerializer.Deserialize<List<FormatItemDto>>(row.FormatItemsJson, SerializerSettings) ?? new List<FormatItemDto>();
            var formatItems = new List<ProfileFormatItem>();

            foreach (var dto in dtos)
            {
                if (cfs.TryGetValue(dto.Format, out var format))
                {
                    formatItems.Add(new ProfileFormatItem { Format = format, Score = dto.Score });
                }
            }

            return new QualityProfile
            {
                Id = row.Id,
                Name = row.Name,
                UpgradeAllowed = row.UpgradeAllowed,
                Cutoff = row.Cutoff,
                MinFormatScore = row.MinFormatScore,
                CutoffFormatScore = row.CutoffFormatScore,
                MinUpgradeFormatScore = row.MinUpgradeFormatScore,
                FormatItems = formatItems,
                Items = JsonSerializer.Deserialize<List<QualityProfileQualityItem>>(row.ItemsJson, SerializerSettings) ?? new List<QualityProfileQualityItem>()
            };
        }

        protected override int GetRowId(StdbQualityProfile row) => row.Id;

        // ToModel silently drops any FormatItemDto whose Format id no longer exists in
        // ICustomFormatService (see the loop above) - so profile.FormatItems as returned by
        // All()/Find() can never reveal a stale id for SpacetimeCleanupQualityProfileFormatItems
        // to detect and persist a removal for. This gives that task the raw, unfiltered ids
        // exactly as stored, bypassing the CustomFormat existence check.
        public Dictionary<int, List<int>> GetRawFormatItemIds()
        {
            return RemoteQuery(string.Empty).ToDictionary(
                row => row.Id,
                row => (JsonSerializer.Deserialize<List<FormatItemDto>>(row.FormatItemsJson, SerializerSettings) ?? new List<FormatItemDto>())
                    .Select(dto => dto.Format)
                    .ToList());
        }

        private static string SerializeFormatItems(QualityProfile model) =>
            JsonSerializer.Serialize(model.FormatItems.Select(f => new FormatItemDto { Format = f.Format.Id, Score = f.Score }).ToList(), SerializerSettings);

        private static string SerializeItems(QualityProfile model) =>
            JsonSerializer.Serialize(model.Items, SerializerSettings);

        protected override void InvokeInsertReducer(QualityProfile model) => Conn.Connection.Reducers.InsertQualityProfile(
            model.Name ?? string.Empty,
            model.UpgradeAllowed,
            model.Cutoff,
            model.MinFormatScore,
            model.CutoffFormatScore,
            model.MinUpgradeFormatScore,
            SerializeFormatItems(model),
            SerializeItems(model));

        public override void MigrateInsert(QualityProfile model) => Conn.Connection.Reducers.MigrateInsertQualityProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.UpgradeAllowed,
            model.Cutoff,
            model.MinFormatScore,
            model.CutoffFormatScore,
            model.MinUpgradeFormatScore,
            SerializeFormatItems(model),
            SerializeItems(model));

        protected override void InvokeUpdateReducer(QualityProfile model) => Conn.Connection.Reducers.UpdateQualityProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.UpgradeAllowed,
            model.Cutoff,
            model.MinFormatScore,
            model.CutoffFormatScore,
            model.MinUpgradeFormatScore,
            SerializeFormatItems(model),
            SerializeItems(model));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteQualityProfile(id);

        public bool Exists(int id) => Find(id) != null;
    }
}
