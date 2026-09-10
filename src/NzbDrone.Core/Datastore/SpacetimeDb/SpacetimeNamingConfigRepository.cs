using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using StdbNamingConfig = SpacetimeDB.Types.NamingConfig;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeNamingConfigRepository : SpacetimeBasicRepository<NamingConfig, StdbNamingConfig>, INamingConfigRepository
    {
        public SpacetimeNamingConfigRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbNamingConfig[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.NamingConfig.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override NamingConfig ToModel(StdbNamingConfig row) => new NamingConfig
        {
            Id = row.Id,
            RenameEpisodes = row.RenameEpisodes,
            ReplaceIllegalCharacters = row.ReplaceIllegalCharacters,
            ColonReplacementFormat = (ColonReplacementFormat)row.ColonReplacementFormat,
            CustomColonReplacementFormat = row.CustomColonReplacementFormat,
            MultiEpisodeStyle = (MultiEpisodeStyle)row.MultiEpisodeStyle,
            StandardEpisodeFormat = row.StandardEpisodeFormat,
            DailyEpisodeFormat = row.DailyEpisodeFormat,
            AnimeEpisodeFormat = row.AnimeEpisodeFormat,
            SeriesFolderFormat = row.SeriesFolderFormat,
            SeasonFolderFormat = row.SeasonFolderFormat,
            SpecialsFolderFormat = row.SpecialsFolderFormat
        };

        protected override int GetRowId(StdbNamingConfig row) => row.Id;

        protected override void InvokeInsertReducer(NamingConfig model) => Conn.Connection.Reducers.InsertNamingConfig(
            model.RenameEpisodes,
            model.ReplaceIllegalCharacters,
            (int)model.ColonReplacementFormat,
            model.CustomColonReplacementFormat,
            (int)model.MultiEpisodeStyle,
            model.StandardEpisodeFormat,
            model.DailyEpisodeFormat,
            model.AnimeEpisodeFormat,
            model.SeriesFolderFormat,
            model.SeasonFolderFormat,
            model.SpecialsFolderFormat);

        protected override void InvokeUpdateReducer(NamingConfig model) => Conn.Connection.Reducers.UpdateNamingConfig(
            model.Id,
            model.RenameEpisodes,
            model.ReplaceIllegalCharacters,
            (int)model.ColonReplacementFormat,
            model.CustomColonReplacementFormat,
            (int)model.MultiEpisodeStyle,
            model.StandardEpisodeFormat,
            model.DailyEpisodeFormat,
            model.AnimeEpisodeFormat,
            model.SeriesFolderFormat,
            model.SeasonFolderFormat,
            model.SpecialsFolderFormat);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteNamingConfig(id);
    }
}
