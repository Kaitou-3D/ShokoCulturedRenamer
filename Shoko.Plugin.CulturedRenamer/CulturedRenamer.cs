﻿
using System.Text;
using System.Transactions;
using Microsoft.Extensions.Logging;
using Shoko.Plugin.Abstractions;
using Shoko.Plugin.Abstractions.Attributes;
using Shoko.Plugin.Abstractions.DataModels;


namespace Renamer.CulturedRenamer;


/// <summary>
/// Cultured Renamer
/// Target Folder Structure is based on type (OVA, Movies, Series) and Restricted > 18+
/// </summary>
[RenamerID("CulturedRenamer")]
public class CulturedRenamer : IRenamer
{

        // Use Microsoft.Extensions.Logging. The Dependency Injection container will inject the logger.
    private readonly ILogger<CulturedRenamer> _logger;

    /// <summary>
    /// Preferred Title Languages
    /// </summary>
    private TitleLanguage[] _preferredLangs =
    {
        TitleLanguage.English,
        TitleLanguage.Romaji
    };

    private string _AnimeDir = "Anime";

    private string _AnimeMovieDir = "Movies";

    private string _AnimeHentaiDir = "Hentai";

    public string Name => GetType().Name;

    public string Description => "Target Folder Structure is based on type (OVA, Movies, Series) and Restricted > 18+";

    public bool SupportsMoving => true;

    public bool SupportsRenaming => true;

    public CulturedRenamer(ILogger<CulturedRenamer> logger)
    {
        _logger = logger;
    }

    public RelocationResult GetNewPath(RelocationEventArgs args)
    {
        var filename = GetFilename(args);
        if (string.IsNullOrEmpty(filename))
        {
            _logger.LogError($"Unable to get new filename for {args.FileInfo.FileName}");
            return new RelocationResult { Error = new MoveRenameError("Filename is empty") };
        }
        var destination = GetDestination(args);
        {
            _logger.LogError($"Unable to get new destination for {args.FileInfo.FileName}");
            if (destination == default) return new RelocationResult { Error = new MoveRenameError("Destination is empty") };
        }

        return new RelocationResult
        {
            FileName = filename,
            Path = destination.subfolder,
            DestinationImportFolder = destination.destination
        };
    }

    public string GetFilename(RelocationEventArgs args)
    {
        //make args.FileInfo easier accessible. this refers to the actual file
        var video = args.FileInfo;

        //make the episode in question easier accessible. this refers to the episode the file is linked to
        var episode = args.EpisodeInfo.First();

        //make the anime the episode belongs to easier accessible.
        var anime = args.AnimeInfo.First();

        //start a string builder with the title of the anime
        var name = new StringBuilder(GetTitleByPref(anime,
                                    TitleType.Official, _preferredLangs));

        string pattern = string.Empty;

        if (anime.Type != AnimeType.Movie)
        {
            pattern = episode.Type switch
            {
                EpisodeType.Episode => episode.EpisodeNumber.
                                        PadZeroes(anime.EpisodeCounts.Episodes),
                EpisodeType.Credits => episode.EpisodeNumber.
                                        PadZeroes(anime.EpisodeCounts.Credits),
                EpisodeType.Special => episode.EpisodeNumber.
                                        PadZeroes(anime.EpisodeCounts.Specials),
                EpisodeType.Trailer => episode.EpisodeNumber.
                                        PadZeroes(anime.EpisodeCounts.Trailers),
                EpisodeType.Parody => episode.EpisodeNumber.
                                        PadZeroes(anime.EpisodeCounts.Parodies),
                EpisodeType.Other => episode.EpisodeNumber.
                                        PadZeroes(anime.EpisodeCounts.Others)

            };
        }
        //get the preferred episode name and add it to the name
        name.Append($" - {GetEpNameByPref(episode, _preferredLangs)}");
        //after this: name = Showname - S03 - SpecialName

        var _subCount = args.FileInfo.VideoInfo.MediaInfo.Subs.Count();
        if(_subCount > 0)
        {
            if(_subCount > 1)
                name.Append("- [MULTI]");
            else
                name.Append($"[{args.FileInfo.VideoInfo.MediaInfo.Subs[0].LanguageCode}]");
        }
        //after this: name = Showname - S03 - SpecialName[sub]

        //get and append the files extension
        name.Append($"{Path.GetExtension(video.FileName)}");
        //after this: name = Showname - S03 - Specialname.mkv

        return name.ToString().ReplaceInvalidPathCharacters();

    }

    /// <summary>
    /// Get anime title as specified by preference. Order matters. if nothing found, preferred title is returned
    /// </summary>
    /// <param name="anime">IAnime object representing the Anime the title has to be searched for</param>
    /// <param name="type">TitleType, eg. TitleType.Official or TitleType.Short </param>
    /// <param name="langs">Arguments Array taking in the TitleLanguages that should be search for.</param>
    /// <returns>string representing the Anime Title for the first language a title is found for</returns>
    private string GetTitleByPref(ISeries anime, TitleType type, params TitleLanguage[] langs)
    {
        //get all titles
        var titles = anime.Titles;
        var title = string.Empty;
        //iterate over the given TitleLanguages in langs
        foreach (var lang in langs)
        {
            //set title to the first found title of the defined language. if nothing found title will stay null
            title = titles.FirstOrDefault(s => s.Language == lang && s.Type == type)?.Title;

        }

        // if no title found for the preferred languages, return the preferred title as defined by shoko
        return title != null ? title : anime.PreferredTitle;
    }

    /// <summary>
    /// Get the new path for a specified file.
    /// The target path depends on <see cref="AnimeType" & restricted status
    /// </summary>
    /// <param name="args">Arguments for the process, contains FileInfo and more</param>
    public (IImportFolder destination, string subfolder) GetDestination(RelocationEventArgs args)
    {
         //get the anime the file in question is linked to
        var anime = args.AnimeInfo.First();

        //get the FileInfo of the file in question
        var video = args.FileInfo.VideoInfo;

        //check if the anime in question is restricted to 18+
        var isPorn = anime.Restricted;

        // Determine if this is a series or a movie
        var location = anime.Type == AnimeType.Movie ?  _AnimeMovieDir : _AnimeDir;

        // Porn rules them all
        location = isPorn ? _AnimeHentaiDir : location;
        _logger.LogInformation($"Looking for {location}.");
        var destFolder = args.AvailableFolders
                                .FirstOrDefault( folder => folder.Name.ToLower() == location.ToLower());

        // DestinationPath is the name of the final subfolder containing the episode files. Get it by preference
        var destSubFolder = GetTitleByPref(anime, TitleType.Official, _preferredLangs)
                                .ReplaceInvalidPathCharacters();


        return (destFolder,destSubFolder);
    }



    /// <summary>
    /// Get Episode Name/Title as specified by preference. if nothing found, the first available is returned
    /// </summary>
    /// <param name="episode">IEpisode object representing the episode to search the name for</param>
    /// <param name="langs">Arguments array taking in the TitleLanguages that should be search for.</param>
    /// <returns>string representing the episode name for the first language a name is found for</returns>
    private string GetEpNameByPref(IEpisode episode, params TitleLanguage[] langs)
    {
        //iterate over all passed TitleLanguages
        foreach (var lang in langs)
        {
            //set the title to the first found title whose language matches with the search one.
            //if none is found, title is null
            var title = episode.Titles.FirstOrDefault(s => s.Language == lang)?.Title;

            //return the found title if title is not null
            if (title != null) return title;
        }

        //no title for any given TitleLanguage found, return the first available.
        return episode.Titles.First().Title;
    }

}
