#pragma warning disable CA2007
#pragma warning disable CA1861

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Jellyfin.Plugin.Streamyfin.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace Jellyfin.Plugin.Streamyfin;

public class StreamyfinManager
{
    private readonly ILogger<StreamyfinManager> _logger;
    private readonly IConfigurationManager _config;
    private readonly IFileSystem _fileSystem;
    private readonly IItemRepository _itemRepo;
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;

    public StreamyfinManager(
        ILogger<StreamyfinManager> logger,
        IConfigurationManager config,
        IFileSystem fileSystem,
        IItemRepository itemRepo,
        ILibraryManager libraryManager,
        ICollectionManager collectionManager)
    {
        _logger = logger;
        _config = config;
        _fileSystem = fileSystem;
        _itemRepo = itemRepo;
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
    }

}

