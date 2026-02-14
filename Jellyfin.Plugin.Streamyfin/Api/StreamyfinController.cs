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
  private readonly LocalizationHelper _localizationHelper;

  public StreamyfinController(
    ILoggerFactory loggerFactory,
    IDtoService dtoService,
    IServerConfigurationManager config,
    IUserManager userManager,
    ILibraryManager libraryManager,
    SerializationHelper serializationHelper,
    NotificationHelper notificationHelper,
    LocalizationHelper localizationHelper
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
    _localizationHelper = localizationHelper;

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
    _logger.LogDebug("Posting device token for deviceId: {0}", deviceToken.DeviceId);
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

    _logger.LogDebug("Deleting device token for deviceId: {0}", deviceId);
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
            userId = _userManager.Users.ToList().Find(u => u.Username == notification.Username)?.Id;
          }
          if (userId != null)
          {
            _logger.LogDebug("Getting device tokens associated to userId: {0}", userId);
            tokens.AddRange(
              db?.GetUserDeviceTokens((Guid) userId)
              ?? []
            );
          }
        }
        // Get all available tokens
        else if (!notification.IsAdmin)
        {
          _logger.LogDebug("No user target provided. Getting all device tokens...");
          allTokens ??= db?.GetAllDeviceTokens() ?? [];
          tokens.AddRange(allTokens);
          _logger.LogDebug("All known device tokens count: {0}", allTokens.Count);
        }

        // Get all available tokens for admins
        if (notification.IsAdmin)
        {
          var adminTokens = _userManager.GetAdminDeviceTokens();
          var adminUsernames = _userManager.Users
            .Where(u => u.Permissions.Any(p => p.Kind == Jellyfin.Database.Implementations.Enums.PermissionKind.IsAdministrator && p.Value))
            .Select(u => u.Username)
            .ToList();

          _logger.LogDebug(
            "Notification is for admins - adding {0} admin device tokens from administrators: [{1}]",
            adminTokens.Count,
            string.Join(", ", adminUsernames)
          );

          tokens.AddRange(adminTokens);
        }

        expoNotification.To = tokens.Select(t => t.Token).Distinct().ToList();

        _logger.LogDebug(
          "Notification routing summary - IsAdmin: {0}, TargetUsername: '{1}', TargetUserId: '{2}', Total device tokens: {3}",
          notification.IsAdmin,
          notification.Username ?? "N/A",
          notification.UserId?.ToString() ?? "N/A",
          expoNotification.To.Count
        );

        return expoNotification;
      })
      .Where(n => n.To.Count > 0)
      .ToArray();

    _logger.LogInformation("Received {0} valid notifications", validNotifications.Length);

    if (validNotifications.Length == 0)
    {
      return new AcceptedResult();
    }

    _logger.LogDebug("Posting notifications...");
    var task = _notificationHelper.Send(validNotifications);
    task.Wait();
    return new JsonResult(_serializationHelperService.ToJson(task.Result));
  }

  /// <summary>
  /// Specialized endpoint for Jellyseerr webhook notifications
  /// </summary>
  /// <param name="payload">Jellyseerr webhook payload</param>
  /// <returns>ActionResult with notification result</returns>
  [HttpPost("notification/seerr")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status202Accepted)]
  public ActionResult PostJellyseerrNotification([FromBody, Required] PushNotifications.Models.JellyseerrNotificationPayload payload)
  {
    // Log full payload for debugging
    _logger.LogDebug("Received Jellyseerr notification - Raw payload: {0}", _serializationHelperService.ToJson(payload));
    _logger.LogDebug("NotificationType={0}, Event={1}, Subject={2}", payload.NotificationType, payload.Event, payload.Subject);

    // Create mapper instance
    var mapper = new JellyseerrNotificationMapper(
      _localizationHelper,
      _loggerFactory.CreateLogger<JellyseerrNotificationMapper>()
    );

    // Map Jellyseerr payload to Streamyfin notification
    var notification = mapper.MapToNotification(payload);

    // If notification is null (e.g., issue event), return 202 Accepted
    if (notification == null)
    {
      _logger.LogDebug("Jellyseerr notification ignored");
      return new AcceptedResult();
    }

    // Use the existing notification endpoint logic
    _logger.LogInformation("Processing Jellyseerr notification as Streamyfin notification");
    return PostNotifications(new List<Notification> { notification });
  }
}
