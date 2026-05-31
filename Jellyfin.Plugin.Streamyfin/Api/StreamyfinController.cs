using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.Storage.Models;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.Api;

public class JsonStringResult : ContentResult
{
  public JsonStringResult(string json)
  {
    Content = json;
    ContentType = "application/json";
  }
}

public class ConfigYamlRes
{
  public string Value { get; set; } = default!;
}

public class ConfigSaveResponse
{
  public bool Error { get; set; }
  public string Message { get; set; } = default!;
}

//public class ConfigYamlReq {
//  public string? Value { get; set; }
//}

/// <summary>
/// CollectionImportController.
/// </summary>
[ApiController]
[Route("streamyfin")]
public class StreamyfinController : ControllerBase
{
  private readonly ILogger<StreamyfinController> _logger;
  private readonly ILoggerFactory _loggerFactory;
  private readonly IServerConfigurationManager _config;
  private readonly IUserManager _userManager;
  private readonly ILibraryManager _libraryManager;
  private readonly IDtoService _dtoService;
  private readonly SerializationHelper _serializationHelperService;
  private readonly NotificationHelper _notificationHelper;

  public StreamyfinController(
    ILoggerFactory loggerFactory,
    IDtoService dtoService,
    IServerConfigurationManager config,
    IUserManager userManager,
    ILibraryManager libraryManager,
    SerializationHelper serializationHelper,
    NotificationHelper notificationHelper
  )
  {
    _loggerFactory = loggerFactory;
    _logger = loggerFactory.CreateLogger<StreamyfinController>();
    _dtoService = dtoService;
    _config = config;
    _userManager = userManager;
    _libraryManager = libraryManager;
    _serializationHelperService = serializationHelper;
    _notificationHelper = notificationHelper;

    _logger.LogInformation("StreamyfinController Loaded");
  }

  [HttpPost("config/yaml")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<ConfigSaveResponse> saveConfig(
    [FromBody, Required] ConfigYamlRes config
  )
  {
    Config p;
    try
    {
      p = _serializationHelperService.Deserialize<Config>(config.Value);
    }
    catch (Exception e)
    {

      return new ConfigSaveResponse { Error = true, Message = e.ToString() };
    }

    var c = StreamyfinPlugin.Instance!.Configuration;
    c.Config = p;
    StreamyfinPlugin.Instance!.UpdateConfiguration(c);

    return new ConfigSaveResponse { Error = false };
  }

  [HttpGet("config")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult getConfig()
  {
    var config = StreamyfinPlugin.Instance!.Configuration.Config;
    return new JsonStringResult(_serializationHelperService.SerializeToJson(config));
  }

  [HttpGet("config/schema")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult getConfigSchema(
  )
  {
    return new JsonStringResult(SerializationHelper.GetJsonSchema<Config>());
  }

  [HttpGet("config/yaml")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<ConfigYamlRes> getConfigYaml()
  {
    return new ConfigYamlRes
    {
      Value = _serializationHelperService.SerializeToYaml(StreamyfinPlugin.Instance!.Configuration.Config)
    };
  }
  
  [HttpGet("config/default")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<ConfigYamlRes> getDefaultConfig()
  {
    return new ConfigYamlRes
    {
      Value = _serializationHelperService.SerializeToYaml(PluginConfiguration.DefaultConfig())
    };
  }

  /// <summary>
  /// Post expo push tokens for a specific user & device 
  /// </summary>
  /// <param name="deviceToken"></param>
  [HttpPost("device")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult PostDeviceToken([FromBody, Required] DeviceToken deviceToken)
  {
    _logger.LogInformation("Posting device token for deviceId: {0}", deviceToken.DeviceId);
    return new JsonResult(
      _serializationHelperService.ToJson(StreamyfinPlugin.Instance!.Database.AddDeviceToken(deviceToken))
    );
  }
  
  /// <summary>
  /// Delete expo push tokens for a specific device 
  /// </summary>
  /// <param name="deviceId"></param>
  [HttpDelete("device/{deviceId}")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult DeleteDeviceToken([FromRoute, Required] Guid? deviceId)
  {
    if (deviceId == null) return BadRequest("Device id is required");

    _logger.LogInformation("Deleting device token for deviceId: {0}", deviceId);
    StreamyfinPlugin.Instance!.Database.RemoveDeviceToken((Guid) deviceId);

    return new OkResult();
  }

  /// <summary>
  /// Forward notifications to expos push service using persisted device tokens
  /// </summary>
  /// <param name="notifications"></param>
  /// <returns></returns>
  [HttpPost("notification")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status202Accepted)]
  public ActionResult PostNotifications([FromBody, Required] List<Notification> notifications)
  {
    var db = StreamyfinPlugin.Instance?.Database;

    if (db?.TotalDevicesCount() == 0)
    {
      _logger.LogInformation("There are currently no devices setup to receive push notifications");
      return new AcceptedResult();
    }

    List<DeviceToken>? allTokens = null;
    var validNotifications = notifications
      .FindAll(n =>
      {
        var title = n.Title ?? "";
        var body = n.Body ?? "";
        
        // Title and body are both valid
        if (!title.IsNullOrNonWord() && !body.IsNullOrNonWord())
        {
          return true;
        }

        // Title can be empty, body is required.
        return string.IsNullOrEmpty(title) && !body.IsNullOrNonWord();
        // every other scenario is invalid
      })
      .Select(notification =>
      {
        List<DeviceToken> tokens = [];
        var expoNotification = notification.ToExpoNotification();
        
        // Get tokens for target user
        if (notification.UserId != null || !string.IsNullOrWhiteSpace(notification.Username))
        {
          Guid? userId = null;

          if (notification.UserId != null)
          {
            userId = notification.UserId;
          } 
          else if (notification.Username != null)
          {
            userId = _userManager.GetUsers().ToList().Find(u => u.Username == notification.Username)?.Id;
          }
          if (userId != null)
          {
            _logger.LogInformation("Getting device tokens associated to userId: {0}", userId);
            tokens.AddRange(
              db?.GetUserDeviceTokens((Guid) userId)
              ?? []
            );
          }
        }
        // Get all available tokens
        else if (!notification.IsAdmin)
        {
          _logger.LogInformation("No user target provided. Getting all device tokens...");
          allTokens ??= db?.GetAllDeviceTokens() ?? [];
          tokens.AddRange(allTokens);
          _logger.LogInformation("All known device tokens count: {0}", allTokens.Count);
        }

        // Get all available tokens for admins
        if (notification.IsAdmin)
        {
          _logger.LogInformation("Notification being posted for admins");
          tokens.AddRange(_userManager.GetAdminDeviceTokens());
        }

        expoNotification.To = tokens.Select(t => t.Token).Distinct().ToList();

        return expoNotification;
      })
      .Where(n => n.To.Count > 0)
      .ToArray();

    _logger.LogInformation("Received {0} valid notifications", validNotifications.Length);

    if (validNotifications.Length == 0)
    {
      return new AcceptedResult();
    }

    _logger.LogInformation("Posting notifications...");
    var task = _notificationHelper.Send(validNotifications);
    task.Wait();
    return new JsonResult(_serializationHelperService.ToJson(task.Result));
  }
}
