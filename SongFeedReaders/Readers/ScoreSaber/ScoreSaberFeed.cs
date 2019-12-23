﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SongFeedReaders.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebUtilities;

namespace SongFeedReaders.Readers.ScoreSaber
{
    public class ScoreSaberFeed : IFeed
    {
        private const string TOP_RANKED_KEY = "Top Ranked";
        private const string TRENDING_KEY = "Trending";
        private const string TOP_PLAYED_KEY = "Top Played";
        private const string LATEST_RANKED_KEY = "Latest Ranked";
        private const string SEARCH_KEY = "Search";
        private const string DescriptionTrending = "Retrieves songs that are Trending as determined by ScoreSaber.";
        private const string DescriptionLatestRanked = "Retrieves the latest ranked songs.";
        private const string DescriptionTopPlayed = "Retrieves the songs that have the most plays as determined by ScoreSaber.";
        private const string DescriptionTopRanked = "Retrieves songs ordered from highest ranked value to lowest.";
        private const string DescriptionSearch = "Retrieves songs matching the search criteria from ScoreSaber.";

        private const string PAGENUMKEY = "{PAGENUM}";
        //private static readonly string CATKEY = "{CAT}";
        private const string RANKEDKEY = "{RANKKEY}";
        private const string LIMITKEY = "{LIMIT}";
        private const string QUERYKEY = "{QUERY}";

        private static Dictionary<ScoreSaberFeedName, FeedInfo> _feeds;
        public static Dictionary<ScoreSaberFeedName, FeedInfo> Feeds
        {
            get
            {
                if (_feeds == null)
                {
                    _feeds = new Dictionary<ScoreSaberFeedName, FeedInfo>()
                    {
                        { (ScoreSaberFeedName)0, new FeedInfo(TRENDING_KEY, "ScoreSaber Trending", $"https://scoresaber.com/api.php?function=get-leaderboards&cat=0&limit={LIMITKEY}&page={PAGENUMKEY}&ranked={RANKEDKEY}",DescriptionTrending ) },
                        { (ScoreSaberFeedName)1, new FeedInfo(LATEST_RANKED_KEY, "ScoreSaber Latest Ranked", $"https://scoresaber.com/api.php?function=get-leaderboards&cat=1&limit={LIMITKEY}&page={PAGENUMKEY}&ranked={RANKEDKEY}", DescriptionLatestRanked) },
                        { (ScoreSaberFeedName)2, new FeedInfo(TOP_PLAYED_KEY, "ScoreSaber Top Played", $"https://scoresaber.com/api.php?function=get-leaderboards&cat=2&limit={LIMITKEY}&page={PAGENUMKEY}&ranked={RANKEDKEY}", DescriptionTopPlayed) },
                        { (ScoreSaberFeedName)3, new FeedInfo(TOP_RANKED_KEY, "ScoreSaber Top Ranked", $"https://scoresaber.com/api.php?function=get-leaderboards&cat=3&limit={LIMITKEY}&page={PAGENUMKEY}&ranked={RANKEDKEY}", DescriptionTopRanked) },
                        { (ScoreSaberFeedName)99, new FeedInfo(SEARCH_KEY, "ScoreSaber Search", $"https://scoresaber.com/api.php?function=get-leaderboards&cat=3&limit={LIMITKEY}&page={PAGENUMKEY}&ranked={RANKEDKEY}&search={QUERYKEY}", DescriptionSearch) }
                    };
                }
                return _feeds;
            }
        }

        private static FeedReaderLoggerBase _logger;
        public static FeedReaderLoggerBase Logger
        {
            get { return _logger ?? LoggingController.DefaultLogger; }
            set { _logger = value; }
        }

        public ScoreSaberFeedName Feed { get; }

        public FeedInfo FeedInfo { get; }

        public string Name { get { return FeedInfo.Name; } }

        public string DisplayName { get { return FeedInfo.DisplayName; } }

        public string Description { get { return FeedInfo.Description; } }

        public Uri RootUri => ScoreSaberReader.ReaderRootUri;

        public string BaseUrl { get { return FeedInfo.BaseUrl; } }

        public int SongsPerPage { get; set; }

        public string SearchQuery { get; }

        public bool RankedOnly { get; }

        public bool StoreRawData { get; set; }

        public IFeedSettings Settings => ScoreSaberFeedSettings;
        public ScoreSaberFeedSettings ScoreSaberFeedSettings { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="settings"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public ScoreSaberFeed(ScoreSaberFeedSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings), "settings cannot be null when creating a new ScoreSaberFeed.");
            ScoreSaberFeedSettings = (ScoreSaberFeedSettings)settings.Clone();
            Feed = ScoreSaberFeedSettings.Feed;
            FeedInfo = Feeds[ScoreSaberFeedSettings.Feed];
            SongsPerPage = ScoreSaberFeedSettings.SongsPerPage;
            SearchQuery = ScoreSaberFeedSettings.SearchQuery;
            RankedOnly = ScoreSaberFeedSettings.RankedOnly;
        }

        public async Task<PageReadResult> GetSongsFromPageAsync(int page, CancellationToken cancellationToken)
        {
            if (page < 1) throw new ArgumentOutOfRangeException(nameof(page), "Page cannot be less than 1.");
            Dictionary<string, ScrapedSong> songs = new Dictionary<string, ScrapedSong>();


            var uri = GetUriForPage(page);
            string pageText = "";

            IWebResponseMessage response = null;

            try
            {
                response = await WebUtils.WebClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                pageText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (WebClientException ex)
            {
                string errorText = string.Empty;
                int statusCode = ex?.Response?.StatusCode ?? 0;
                if (statusCode != 0)
                {
                    switch (statusCode)
                    {
                        case 404:
                            errorText = $"{uri.ToString()} was not found.";
                            break;
                        case 408:
                            errorText = $"Timeout getting first page in ScoreSaberReader: {uri}: {ex.Message}";
                            break;
                        default:
                            errorText = $"Site Error getting first page in ScoreSaberReader: {uri}: {ex.Message}";
                            break;
                    }
                }
                Logger?.Debug(errorText);
                // No need for a stacktrace if it's one of these errors.
                if (!(statusCode == 404 || statusCode == 408 || statusCode == 500))
                    Logger?.Debug($"{ex.Message}\n{ex.StackTrace}");
                return PageReadResult.FromWebClientException(ex, uri, page);
            }
            catch (Exception ex)
            {
                string message = $"Uncaught error getting the first page in ScoreSaberReader.GetSongsFromScoreSaberAsync(): {ex.Message}";
                return new PageReadResult(uri, new List<ScrapedSong>(), page, new FeedReaderException(message, ex, FeedReaderFailureCode.SourceFailed), PageErrorType.Unknown);
            }
            finally
            {
                response?.Dispose();
                response = null;
            }
            bool isLastPage;
            try
            {
                var diffs = GetSongsFromPageText(pageText, uri, StoreRawData);
                isLastPage = diffs.Count < SongsPerPage;
                foreach (var diff in diffs)
                {
                    if (!songs.ContainsKey(diff.Hash))
                        songs.Add(diff.Hash, diff);
                }
            }
            catch (JsonReaderException ex)
            {
                string message = "Unable to parse JSON from text";
                Logger?.Debug($"{message}: {ex.Message}\n{ex.StackTrace}");
                return new PageReadResult(uri, null, page, new FeedReaderException(message, ex, FeedReaderFailureCode.PageFailed), PageErrorType.ParsingError);
            }
            catch (Exception ex)
            {
                string message = $"Unhandled exception from GetSongsFromPageText() while parsing {uri}";
                Logger?.Debug($"{message}: {ex.Message}\n{ex.StackTrace}");
                return new PageReadResult(uri, null, page, new FeedReaderException(message, ex, FeedReaderFailureCode.PageFailed), PageErrorType.ParsingError);
            }

            return new PageReadResult(uri, songs.Values.ToList(), page, isLastPage);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pageText"></param>
        /// <param name="sourceUri"></param>
        /// <param name="page"></param>
        /// <returns></returns>
        /// <exception cref="JsonReaderException"></exception>
        /// 
        public static List<ScrapedSong> GetSongsFromPageText(string pageText, Uri sourceUri, bool storeRawData)
        {
            JObject result;
            List<ScrapedSong> songs = new List<ScrapedSong>();
            try
            {
                result = JObject.Parse(pageText);
            }
            catch (JsonReaderException ex)
            {
                throw;
                //string message = "Unable to parse JSON from text";
                //Logger?.Debug($"{message}: {ex.Message}\n{ex.StackTrace}");
                //return new PageReadResult(sourceUri, null, page, new FeedReaderException(message, ex, FeedReaderFailureCode.PageFailed), PageErrorType.ParsingError);
            }
            var songJSONAry = result["songs"]?.ToArray();
            if (songJSONAry == null)
            {
                string message = "Invalid page text: 'songs' field not found.";
                Logger?.Debug(message);
                return null;
            }
            foreach (var song in songJSONAry)
            {
                var hash = song["id"]?.Value<string>();
                var songName = song["name"]?.Value<string>();
                var mapperName = song["levelAuthorName"]?.Value<string>();

                if (!string.IsNullOrEmpty(hash))
                    songs.Add(new ScrapedSong(hash)
                    {
                        DownloadUri = Utilities.GetDownloadUriByHash(hash),
                        SourceUri = sourceUri,
                        SongName = songName,
                        MapperName = mapperName,
                        RawData = storeRawData ? song.ToString(Newtonsoft.Json.Formatting.None) : string.Empty
                    });
            }
            return songs;
        }

        public Uri GetUriForPage(int page)
        {
            string url = BaseUrl;
            Dictionary<string, string> urlReplacements = new Dictionary<string, string>() {
                {LIMITKEY, SongsPerPage.ToString() },
                {PAGENUMKEY, page.ToString()},
                {RANKEDKEY, RankedOnly ? "1" : "0" }
            };
            if (Feed == ScoreSaberFeedName.Search)
            {
                urlReplacements.Add(QUERYKEY, SearchQuery);
            }
            foreach (var pair in urlReplacements)
            {
                url = url.Replace(pair.Key, pair.Value);
            }
            return new Uri(url);
        }

        public FeedAsyncEnumerator GetEnumerator(bool cachePages)
        {
            return new FeedAsyncEnumerator(this, Settings.StartingPage, cachePages);
        }
        public FeedAsyncEnumerator GetEnumerator()
        {
            return GetEnumerator(false);
        }

        public static List<ScrapedSong> GetSongsFromPageText(string pageText, string sourceUrl, bool storeRawData)
        {
            return GetSongsFromPageText(pageText, new Uri(sourceUrl), storeRawData);
        }
    }
}