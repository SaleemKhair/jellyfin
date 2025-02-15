﻿#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Caching.Memory;
using TMDbLib.Client;
using TMDbLib.Objects.Collections;
using TMDbLib.Objects.Find;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.People;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

namespace MediaBrowser.Providers.Plugins.Tmdb
{
    /// <summary>
    /// Manager class for abstracting the TMDb API client library.
    /// </summary>
    public class TmdbClientManager : IDisposable
    {
        private const int CacheDurationInHours = 1;

        private readonly IMemoryCache _memoryCache;
        private readonly TMDbClient _tmDbClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="TmdbClientManager"/> class.
        /// </summary>
        /// <param name="memoryCache">An instance of <see cref="IMemoryCache"/>.</param>
        public TmdbClientManager(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
            _tmDbClient = new TMDbClient(TmdbUtils.ApiKey);
            // Not really interested in NotFoundException
            _tmDbClient.ThrowApiExceptions = false;
        }

        /// <summary>
        /// Gets a movie from the TMDb API based on its TMDb id.
        /// </summary>
        /// <param name="tmdbId">The movie's TMDb id.</param>
        /// <param name="language">The movie's language.</param>
        /// <param name="imageLanguages">A comma-separated list of image languages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb movie or null if not found.</returns>
        public async Task<Movie> GetMovieAsync(int tmdbId, string language, string imageLanguages, CancellationToken cancellationToken)
        {
            var key = $"movie-{tmdbId.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out Movie movie))
            {
                return movie;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var extraMethods = MovieMethods.Credits | MovieMethods.Releases | MovieMethods.Images | MovieMethods.Videos;
            if (!(Plugin.Instance?.Configuration.ExcludeTagsMovies).GetValueOrDefault())
            {
                extraMethods |= MovieMethods.Keywords;
            }

            movie = await _tmDbClient.GetMovieAsync(
                tmdbId,
                TmdbUtils.NormalizeLanguage(language),
                imageLanguages,
                extraMethods,
                cancellationToken).ConfigureAwait(false);

            if (movie != null)
            {
                _memoryCache.Set(key, movie, TimeSpan.FromHours(CacheDurationInHours));
            }

            return movie;
        }

        /// <summary>
        /// Gets a collection from the TMDb API based on its TMDb id.
        /// </summary>
        /// <param name="tmdbId">The collection's TMDb id.</param>
        /// <param name="language">The collection's language.</param>
        /// <param name="imageLanguages">A comma-separated list of image languages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb collection or null if not found.</returns>
        public async Task<Collection> GetCollectionAsync(int tmdbId, string language, string imageLanguages, CancellationToken cancellationToken)
        {
            var key = $"collection-{tmdbId.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out Collection collection))
            {
                return collection;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            collection = await _tmDbClient.GetCollectionAsync(
                tmdbId,
                TmdbUtils.NormalizeLanguage(language),
                imageLanguages,
                CollectionMethods.Images,
                cancellationToken).ConfigureAwait(false);

            if (collection != null)
            {
                _memoryCache.Set(key, collection, TimeSpan.FromHours(CacheDurationInHours));
            }

            return collection;
        }

        /// <summary>
        /// Gets a tv show from the TMDb API based on its TMDb id.
        /// </summary>
        /// <param name="tmdbId">The tv show's TMDb id.</param>
        /// <param name="language">The tv show's language.</param>
        /// <param name="imageLanguages">A comma-separated list of image languages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb tv show information or null if not found.</returns>
        public async Task<TvShow> GetSeriesAsync(int tmdbId, string language, string imageLanguages, CancellationToken cancellationToken)
        {
            var key = $"series-{tmdbId.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out TvShow series))
            {
                return series;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var extraMethods = TvShowMethods.Credits | TvShowMethods.Images | TvShowMethods.ExternalIds | TvShowMethods.Videos | TvShowMethods.ContentRatings | TvShowMethods.EpisodeGroups;
            if (!(Plugin.Instance?.Configuration.ExcludeTagsSeries).GetValueOrDefault())
            {
                extraMethods |= TvShowMethods.Keywords;
            }

            series = await _tmDbClient.GetTvShowAsync(
                tmdbId,
                language: TmdbUtils.NormalizeLanguage(language),
                includeImageLanguage: imageLanguages,
                extraMethods: extraMethods,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (series != null)
            {
                _memoryCache.Set(key, series, TimeSpan.FromHours(CacheDurationInHours));
            }

            return series;
        }

        /// <summary>
        /// Gets a tv show episode group from the TMDb API based on the show id and the display order.
        /// </summary>
        /// <param name="tvShowId">The tv show's TMDb id.</param>
        /// <param name="displayOrder">The display order.</param>
        /// <param name="language">The tv show's language.</param>
        /// <param name="imageLanguages">A comma-separated list of image languages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb tv show episode group information or null if not found.</returns>
        private async Task<TvGroupCollection> GetSeriesGroupAsync(int tvShowId, string displayOrder, string language, string imageLanguages, CancellationToken cancellationToken)
        {
            TvGroupType? groupType =
                string.Equals(displayOrder, "absolute", StringComparison.Ordinal) ? TvGroupType.Absolute :
                string.Equals(displayOrder, "dvd", StringComparison.Ordinal) ? TvGroupType.DVD :
                null;

            if (groupType == null)
            {
                return null;
            }

            var key = $"group-{tvShowId.ToString(CultureInfo.InvariantCulture)}-{displayOrder}-{language}";
            if (_memoryCache.TryGetValue(key, out TvGroupCollection group))
            {
                return group;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var series = await GetSeriesAsync(tvShowId, language, imageLanguages, cancellationToken).ConfigureAwait(false);
            var episodeGroupId = series?.EpisodeGroups.Results.Find(g => g.Type == groupType)?.Id;

            if (episodeGroupId == null)
            {
                return null;
            }

            group = await _tmDbClient.GetTvEpisodeGroupsAsync(
                episodeGroupId,
                language: TmdbUtils.NormalizeLanguage(language),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (group != null)
            {
                _memoryCache.Set(key, group, TimeSpan.FromHours(CacheDurationInHours));
            }

            return group;
        }

        /// <summary>
        /// Gets a tv season from the TMDb API based on the tv show's TMDb id.
        /// </summary>
        /// <param name="tvShowId">The tv season's TMDb id.</param>
        /// <param name="seasonNumber">The season number.</param>
        /// <param name="language">The tv season's language.</param>
        /// <param name="imageLanguages">A comma-separated list of image languages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb tv season information or null if not found.</returns>
        public async Task<TvSeason> GetSeasonAsync(int tvShowId, int seasonNumber, string language, string imageLanguages, CancellationToken cancellationToken)
        {
            var key = $"season-{tvShowId.ToString(CultureInfo.InvariantCulture)}-s{seasonNumber.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out TvSeason season))
            {
                return season;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            season = await _tmDbClient.GetTvSeasonAsync(
                tvShowId,
                seasonNumber,
                language: TmdbUtils.NormalizeLanguage(language),
                includeImageLanguage: imageLanguages,
                extraMethods: TvSeasonMethods.Credits | TvSeasonMethods.Images | TvSeasonMethods.ExternalIds | TvSeasonMethods.Videos,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (season != null)
            {
                _memoryCache.Set(key, season, TimeSpan.FromHours(CacheDurationInHours));
            }

            return season;
        }

        /// <summary>
        /// Gets a movie from the TMDb API based on the tv show's TMDb id.
        /// </summary>
        /// <param name="tvShowId">The tv show's TMDb id.</param>
        /// <param name="seasonNumber">The season number.</param>
        /// <param name="episodeNumber">The episode number.</param>
        /// <param name="displayOrder">The display order.</param>
        /// <param name="language">The episode's language.</param>
        /// <param name="imageLanguages">A comma-separated list of image languages.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb tv episode information or null if not found.</returns>
        public async Task<TvEpisode> GetEpisodeAsync(int tvShowId, int seasonNumber, int episodeNumber, string displayOrder, string language, string imageLanguages, CancellationToken cancellationToken)
        {
            var key = $"episode-{tvShowId.ToString(CultureInfo.InvariantCulture)}-s{seasonNumber.ToString(CultureInfo.InvariantCulture)}e{episodeNumber.ToString(CultureInfo.InvariantCulture)}-{displayOrder}-{language}";
            if (_memoryCache.TryGetValue(key, out TvEpisode episode))
            {
                return episode;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var group = await GetSeriesGroupAsync(tvShowId, displayOrder, language, imageLanguages, cancellationToken).ConfigureAwait(false);
            if (group != null)
            {
                var season = group.Groups.Find(s => s.Order == seasonNumber);
                // Episode order starts at 0
                var ep = season?.Episodes.Find(e => e.Order == episodeNumber - 1);
                if (ep != null)
                {
                    seasonNumber = ep.SeasonNumber;
                    episodeNumber = ep.EpisodeNumber;
                }
            }

            episode = await _tmDbClient.GetTvEpisodeAsync(
                tvShowId,
                seasonNumber,
                episodeNumber,
                language: TmdbUtils.NormalizeLanguage(language),
                includeImageLanguage: imageLanguages,
                extraMethods: TvEpisodeMethods.Credits | TvEpisodeMethods.Images | TvEpisodeMethods.ExternalIds | TvEpisodeMethods.Videos,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (episode != null)
            {
                _memoryCache.Set(key, episode, TimeSpan.FromHours(CacheDurationInHours));
            }

            return episode;
        }

        /// <summary>
        /// Gets a person eg. cast or crew member from the TMDb API based on its TMDb id.
        /// </summary>
        /// <param name="personTmdbId">The person's TMDb id.</param>
        /// <param name="language">The episode's language.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb person information or null if not found.</returns>
        public async Task<Person> GetPersonAsync(int personTmdbId, string language, CancellationToken cancellationToken)
        {
            var key = $"person-{personTmdbId.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out Person person))
            {
                return person;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            person = await _tmDbClient.GetPersonAsync(
                personTmdbId,
                TmdbUtils.NormalizeLanguage(language),
                PersonMethods.TvCredits | PersonMethods.MovieCredits | PersonMethods.Images | PersonMethods.ExternalIds,
                cancellationToken).ConfigureAwait(false);

            if (person != null)
            {
                _memoryCache.Set(key, person, TimeSpan.FromHours(CacheDurationInHours));
            }

            return person;
        }

        /// <summary>
        /// Gets an item from the TMDb API based on its id from an external service eg. IMDb id, TvDb id.
        /// </summary>
        /// <param name="externalId">The item's external id.</param>
        /// <param name="source">The source of the id eg. IMDb.</param>
        /// <param name="language">The item's language.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb item or null if not found.</returns>
        public async Task<FindContainer> FindByExternalIdAsync(
            string externalId,
            FindExternalSource source,
            string language,
            CancellationToken cancellationToken)
        {
            var key = $"find-{source.ToString()}-{externalId.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out FindContainer result))
            {
                return result;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            result = await _tmDbClient.FindAsync(
                source,
                externalId,
                TmdbUtils.NormalizeLanguage(language),
                cancellationToken).ConfigureAwait(false);

            if (result != null)
            {
                _memoryCache.Set(key, result, TimeSpan.FromHours(CacheDurationInHours));
            }

            return result;
        }

        /// <summary>
        /// Searches for a tv show using the TMDb API based on its name.
        /// </summary>
        /// <param name="name">The name of the tv show.</param>
        /// <param name="language">The tv show's language.</param>
        /// <param name="year">The year the tv show first aired.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb tv show information.</returns>
        public async Task<IReadOnlyList<SearchTv>> SearchSeriesAsync(string name, string language, int year = 0, CancellationToken cancellationToken = default)
        {
            var key = $"searchseries-{name}-{language}";
            if (_memoryCache.TryGetValue(key, out SearchContainer<SearchTv> series))
            {
                return series.Results;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var searchResults = await _tmDbClient
                .SearchTvShowAsync(name, TmdbUtils.NormalizeLanguage(language), includeAdult: Plugin.Instance.Configuration.IncludeAdult, firstAirDateYear: year, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (searchResults.Results.Count > 0)
            {
                _memoryCache.Set(key, searchResults, TimeSpan.FromHours(CacheDurationInHours));
            }

            return searchResults.Results;
        }

        /// <summary>
        /// Searches for a person based on their name using the TMDb API.
        /// </summary>
        /// <param name="name">The name of the person.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb person information.</returns>
        public async Task<IReadOnlyList<SearchPerson>> SearchPersonAsync(string name, CancellationToken cancellationToken)
        {
            var key = $"searchperson-{name}";
            if (_memoryCache.TryGetValue(key, out SearchContainer<SearchPerson> person))
            {
                return person.Results;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var searchResults = await _tmDbClient
                .SearchPersonAsync(name, includeAdult: Plugin.Instance.Configuration.IncludeAdult, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (searchResults.Results.Count > 0)
            {
                _memoryCache.Set(key, searchResults, TimeSpan.FromHours(CacheDurationInHours));
            }

            return searchResults.Results;
        }

        /// <summary>
        /// Searches for a movie based on its name using the TMDb API.
        /// </summary>
        /// <param name="name">The name of the movie.</param>
        /// <param name="language">The movie's language.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb movie information.</returns>
        public Task<IReadOnlyList<SearchMovie>> SearchMovieAsync(string name, string language, CancellationToken cancellationToken)
        {
            return SearchMovieAsync(name, 0, language, cancellationToken);
        }

        /// <summary>
        /// Searches for a movie based on its name using the TMDb API.
        /// </summary>
        /// <param name="name">The name of the movie.</param>
        /// <param name="year">The release year of the movie.</param>
        /// <param name="language">The movie's language.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb movie information.</returns>
        public async Task<IReadOnlyList<SearchMovie>> SearchMovieAsync(string name, int year, string language, CancellationToken cancellationToken)
        {
            var key = $"moviesearch-{name}-{year.ToString(CultureInfo.InvariantCulture)}-{language}";
            if (_memoryCache.TryGetValue(key, out SearchContainer<SearchMovie> movies))
            {
                return movies.Results;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var searchResults = await _tmDbClient
                .SearchMovieAsync(name, TmdbUtils.NormalizeLanguage(language), includeAdult: Plugin.Instance.Configuration.IncludeAdult, year: year, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (searchResults.Results.Count > 0)
            {
                _memoryCache.Set(key, searchResults, TimeSpan.FromHours(CacheDurationInHours));
            }

            return searchResults.Results;
        }

        /// <summary>
        /// Searches for a collection based on its name using the TMDb API.
        /// </summary>
        /// <param name="name">The name of the collection.</param>
        /// <param name="language">The collection's language.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The TMDb collection information.</returns>
        public async Task<IReadOnlyList<SearchCollection>> SearchCollectionAsync(string name, string language, CancellationToken cancellationToken)
        {
            var key = $"collectionsearch-{name}-{language}";
            if (_memoryCache.TryGetValue(key, out SearchContainer<SearchCollection> collections))
            {
                return collections.Results;
            }

            await EnsureClientConfigAsync().ConfigureAwait(false);

            var searchResults = await _tmDbClient
                .SearchCollectionAsync(name, TmdbUtils.NormalizeLanguage(language), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (searchResults.Results.Count > 0)
            {
                _memoryCache.Set(key, searchResults, TimeSpan.FromHours(CacheDurationInHours));
            }

            return searchResults.Results;
        }

        /// <summary>
        /// Handles bad path checking and builds the absolute url.
        /// </summary>
        /// <param name="size">The image size to fetch.</param>
        /// <param name="path">The relative URL of the image.</param>
        /// <returns>The absolute URL.</returns>
        private string GetUrl(string size, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            return _tmDbClient.GetImageUrl(size, path).ToString();
        }

        /// <summary>
        /// Gets the absolute URL of the poster.
        /// </summary>
        /// <param name="posterPath">The relative URL of the poster.</param>
        /// <returns>The absolute URL.</returns>
        public string GetPosterUrl(string posterPath)
        {
            return GetUrl(_tmDbClient.Config.Images.PosterSizes[^1], posterPath);
        }

        /// <summary>
        /// Gets the absolute URL of the profile image.
        /// </summary>
        /// <param name="actorProfilePath">The relative URL of the profile image.</param>
        /// <returns>The absolute URL.</returns>
        public string GetProfileUrl(string actorProfilePath)
        {
            return GetUrl(_tmDbClient.Config.Images.ProfileSizes[^1], actorProfilePath);
        }

        /// <summary>
        /// Converts poster <see cref="ImageData"/>s into <see cref="RemoteImageInfo"/>s.
        /// </summary>
        /// <param name="images">The input images.</param>
        /// <param name="requestLanguage">The requested language.</param>
        /// <param name="results">The collection to add the remote images into.</param>
        public void ConvertPostersToRemoteImageInfo(List<ImageData> images, string requestLanguage, List<RemoteImageInfo> results)
        {
            ConvertToRemoteImageInfo(images, _tmDbClient.Config.Images.PosterSizes[^1], ImageType.Primary, requestLanguage, results);
        }

        /// <summary>
        /// Converts backdrop <see cref="ImageData"/>s into <see cref="RemoteImageInfo"/>s.
        /// </summary>
        /// <param name="images">The input images.</param>
        /// <param name="requestLanguage">The requested language.</param>
        /// <param name="results">The collection to add the remote images into.</param>
        public void ConvertBackdropsToRemoteImageInfo(List<ImageData> images, string requestLanguage, List<RemoteImageInfo> results)
        {
            ConvertToRemoteImageInfo(images, _tmDbClient.Config.Images.BackdropSizes[^1], ImageType.Backdrop, requestLanguage, results);
        }

        /// <summary>
        /// Converts profile <see cref="ImageData"/>s into <see cref="RemoteImageInfo"/>s.
        /// </summary>
        /// <param name="images">The input images.</param>
        /// <param name="requestLanguage">The requested language.</param>
        /// <param name="results">The collection to add the remote images into.</param>
        public void ConvertProfilesToRemoteImageInfo(List<ImageData> images, string requestLanguage, List<RemoteImageInfo> results)
        {
            ConvertToRemoteImageInfo(images, _tmDbClient.Config.Images.ProfileSizes[^1], ImageType.Primary, requestLanguage, results);
        }

        /// <summary>
        /// Converts still <see cref="ImageData"/>s into <see cref="RemoteImageInfo"/>s.
        /// </summary>
        /// <param name="images">The input images.</param>
        /// <param name="requestLanguage">The requested language.</param>
        /// <param name="results">The collection to add the remote images into.</param>
        public void ConvertStillsToRemoteImageInfo(List<ImageData> images, string requestLanguage, List<RemoteImageInfo> results)
        {
            ConvertToRemoteImageInfo(images, _tmDbClient.Config.Images.StillSizes[^1], ImageType.Primary, requestLanguage, results);
        }

        /// <summary>
        /// Converts <see cref="ImageData"/>s into <see cref="RemoteImageInfo"/>s.
        /// </summary>
        /// <param name="images">The input images.</param>
        /// <param name="size">The size of the image to fetch.</param>
        /// <param name="type">The type of the image.</param>
        /// <param name="requestLanguage">The requested language.</param>
        /// <param name="results">The collection to add the remote images into.</param>
        private void ConvertToRemoteImageInfo(List<ImageData> images, string size, ImageType type, string requestLanguage, List<RemoteImageInfo> results)
        {
            for (var i = 0; i < images.Count; i++)
            {
                var image = images[i];
                results.Add(new RemoteImageInfo
                {
                    Url = GetUrl(size, image.FilePath),
                    CommunityRating = image.VoteAverage,
                    VoteCount = image.VoteCount,
                    Width = image.Width,
                    Height = image.Height,
                    Language = TmdbUtils.AdjustImageLanguage(image.Iso_639_1, requestLanguage),
                    ProviderName = TmdbUtils.ProviderName,
                    Type = type,
                    RatingType = RatingType.Score
                });
            }
        }

        private Task EnsureClientConfigAsync()
        {
            return !_tmDbClient.HasConfig ? _tmDbClient.GetConfigAsync() : Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _memoryCache?.Dispose();
                _tmDbClient?.Dispose();
            }
        }
    }
}
