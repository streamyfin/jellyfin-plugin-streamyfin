using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Extensions;
using Jellyfin.Plugin.Streamyfin.PushNotifications;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.Db;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
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
/// The plugin's HTTP surface.
/// </summary>
/// <remarks>
/// Every route exists twice: once under <c>v1/</c>, which is canonical, and once at
/// the path it has always had. The unversioned ones are shims and nothing new should
/// use them.
///
/// <para>
/// They are extra attributes on the same action rather than separate methods that
/// delegate. Two methods drift: one gets a fix, the other does not, and the shim
/// quietly stops behaving like the route it stands in for.
/// </para>
///
/// <para>
/// The prefix is what makes the next change to this surface a choice rather than a
/// breaking one. Every app in the field calls the unversioned paths, and until now
/// renaming any of them would have broken every installed copy at once. That is also
/// why <c>device</c> and <c>notification</c> keep working alongside the plural names
/// they should have had.
/// </para>
///
/// <para>
/// <c>ApiSurfaceTests</c> is what keeps this true, since a route dropped from a shim
/// is not a failure anything else would notice until an old client hits a 404.
/// </para>
/// </remarks>
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

  [HttpPost("v1/config/yaml")]

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

    StreamyfinPlugin.Instance!.Settings.Save(p);

    return new ConfigSaveResponse { Error = false };
  }

  /// <summary>
  /// The configuration, as the calling user should receive it.
  /// </summary>
  /// <returns>The configuration.</returns>
  /// <remarks>
  /// An administrator gets it untouched. Anyone else gets their settings resolved
  /// across the three targeting levels, with credentials removed and the server side
  /// blocks left out. Until P1.4 this served the whole configuration, Seerr admin key
  /// included, to every account on the server.
  /// </remarks>
  [HttpGet("v1/config")]
  [HttpGet("config")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult getConfig()
  {
    return new JsonStringResult(_serializationHelperService.SerializeToJson(ConfigForCaller()));
  }

  [HttpGet("v1/config/schema")]

  [HttpGet("config/schema")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult getConfigSchema(
  )
  {
    return new JsonStringResult(SerializationHelper.GetJsonSchema<Config>());
  }

  /// <summary>
  /// The configuration as YAML, filtered the same way as the JSON.
  /// </summary>
  /// <returns>The configuration.</returns>
  [HttpGet("v1/config/yaml")]
  [HttpGet("config/yaml")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<ConfigYamlRes> getConfigYaml()
  {
    return new ConfigYamlRes
    {
      Value = _serializationHelperService.SerializeToYaml(ConfigForCaller())
    };
  }
  
  [HttpGet("v1/config/default")]
  
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
  /// Post expo push tokens for a specific user and device
  /// </summary>
  /// <param name="deviceToken"></param>
  [HttpPost("v1/devices")]
  [HttpPost("v1/device")]
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
  [HttpDelete("v1/devices/{deviceId}")]
  [HttpDelete("v1/device/{deviceId}")]
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
  [HttpPost("v1/notifications")]
  [HttpPost("v1/notification")]
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

  // region Settings groups
  //
  // The three targeting levels of P1: what the server declares for everyone, the
  // groups an administrator defines, and anything aimed at one user. Everything
  // that writes is elevation only. The one route that reads is the caller's own
  // resolved set.

  private const string UserIdClaim = "Jellyfin-UserId";
  private const string IsApiKeyClaim = "Jellyfin-IsApiKey";

  private Guid CallerId =>
    Guid.TryParse(User?.FindFirst(UserIdClaim)?.Value, out var id) ? id : Guid.Empty;

  // An API key is granted by an administrator and carries no user, which is why
  // Jellyfin's own RequiresElevation accepts one. Treated the same here, or the
  // routes an admin can call with their account would answer differently to the
  // key they created for a script.
  private bool CallerIsApiKey =>
    bool.TryParse(User?.FindFirst(IsApiKeyClaim)?.Value, out var isApiKey) && isApiKey;

  private SettingsResolutionService Resolution =>
    new(_serializationHelperService, _loggerFactory.CreateLogger<SettingsResolutionService>());

  /// <summary>
  /// Lists the settings groups.
  /// </summary>
  /// <returns>The groups, least specific first, each with its members.</returns>
  [HttpGet("v1/groups")]
  [HttpGet("groups")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<List<SettingsGroupDto>> GetSettingsGroups()
  {
    var database = StreamyfinPlugin.Instance!.Database;

    return database.GetSettingsGroups()
      .Select(group => ToDto(group, database.GetGroupMembers(group.Id)))
      .ToList();
  }

  /// <summary>
  /// Creates a settings group.
  /// </summary>
  /// <param name="request">The group to create. Its id is ignored.</param>
  /// <returns>The created group.</returns>
  [HttpPost("v1/groups")]
  [HttpPost("groups")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public ActionResult<SettingsGroupDto> CreateSettingsGroup([FromBody, Required] SettingsGroupDto request)
  {
    ArgumentNullException.ThrowIfNull(request);

    if (string.IsNullOrWhiteSpace(request.Name))
    {
      return BadRequest("A group needs a name");
    }

    var database = StreamyfinPlugin.Instance!.Database;

    var stored = database.SaveSettingsGroup(new SettingsGroup
    {
      Id = Guid.Empty,
      Name = request.Name,
      Priority = request.Priority,
      SettingsJson = _serializationHelperService.SerializeToJson(request.Settings ?? new Configuration.Settings.Settings())
    });

    database.SetGroupMembers(stored.Id, request.UserIds);

    return ToDto(stored, database.GetGroupMembers(stored.Id));
  }

  /// <summary>
  /// Updates a settings group.
  /// </summary>
  /// <param name="id">The group id.</param>
  /// <param name="request">What it should become. Members are left alone.</param>
  /// <returns>The updated group.</returns>
  [HttpPut("v1/groups/{id}")]
  [HttpPut("groups/{id}")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public ActionResult<SettingsGroupDto> UpdateSettingsGroup(
    [FromRoute, Required] Guid id,
    [FromBody, Required] SettingsGroupDto request)
  {
    ArgumentNullException.ThrowIfNull(request);

    if (string.IsNullOrWhiteSpace(request.Name))
    {
      return BadRequest("A group needs a name");
    }

    var database = StreamyfinPlugin.Instance!.Database;

    if (database.GetSettingsGroup(id) is null)
    {
      return NotFound();
    }

    var stored = database.SaveSettingsGroup(new SettingsGroup
    {
      Id = id,
      Name = request.Name,
      Priority = request.Priority,
      SettingsJson = _serializationHelperService.SerializeToJson(request.Settings ?? new Configuration.Settings.Settings())
    });

    return ToDto(stored, database.GetGroupMembers(id));
  }

  /// <summary>
  /// Deletes a settings group and everyone's membership of it.
  /// </summary>
  /// <param name="id">The group id.</param>
  /// <returns>No content.</returns>
  [HttpDelete("v1/groups/{id}")]
  [HttpDelete("groups/{id}")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public ActionResult DeleteSettingsGroup([FromRoute, Required] Guid id)
  {
    StreamyfinPlugin.Instance!.Database.RemoveSettingsGroup(id);
    return NoContent();
  }

  /// <summary>
  /// Replaces the membership of a group.
  /// </summary>
  /// <param name="id">The group id.</param>
  /// <param name="request">Who should be in it afterwards.</param>
  /// <returns>The group, with its new members.</returns>
  [HttpPut("v1/groups/{id}/members")]
  [HttpPut("groups/{id}/members")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public ActionResult<SettingsGroupDto> SetSettingsGroupMembers(
    [FromRoute, Required] Guid id,
    [FromBody, Required] SettingsGroupMembersDto request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var database = StreamyfinPlugin.Instance!.Database;
    var group = database.GetSettingsGroup(id);

    if (group is null)
    {
      return NotFound();
    }

    database.SetGroupMembers(id, request.UserIds);
    return ToDto(group, database.GetGroupMembers(id));
  }

  /// <summary>
  /// The settings targeted at one user.
  /// </summary>
  /// <param name="userId">The Jellyfin user id.</param>
  /// <returns>The settings, in an object that carries none when the user has no override.</returns>
  /// <remarks>
  /// Always a JSON object and never an empty body: a user with no override answers with
  /// the settings simply absent. That is what lets the targeting screen read every answer
  /// the same way instead of special casing one of them.
  ///
  /// <para>
  /// The only one of the targeting routes with no unversioned shim, because it is the
  /// only one that was never served: P1.2 gave this level a write and a delete and no
  /// read, which left the targeting screen nothing to open an existing override with.
  /// Read through the same tolerant path the resolution uses, so an override whose JSON
  /// cannot be read still answers rather than failing, and an administrator can see it
  /// to repair it.
  /// </para>
  /// </remarks>
  [HttpGet("v1/users/{userId}/settings")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult<UserSettingsOverrideDto> GetUserSettingsOverride([FromRoute, Required] Guid userId)
  {
    var stored = StreamyfinPlugin.Instance!.Database.GetUserSettingsOverride(userId);

    return new UserSettingsOverrideDto
    {
      Settings = stored is null ? null : Resolution.ReadLevel(stored.SettingsJson, $"user {userId}")
    };
  }

  /// <summary>
  /// Sets the settings targeted at one user.
  /// </summary>
  /// <param name="userId">The Jellyfin user id.</param>
  /// <param name="request">The settings. Send an empty body to clear them.</param>
  /// <returns>No content.</returns>
  [HttpPut("v1/users/{userId}/settings")]
  [HttpPut("users/{userId}/settings")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public ActionResult SetUserSettingsOverride(
    [FromRoute, Required] Guid userId,
    [FromBody, Required] UserSettingsOverrideDto request)
  {
    ArgumentNullException.ThrowIfNull(request);

    var database = StreamyfinPlugin.Instance!.Database;

    if (request.Settings is null)
    {
      database.RemoveUserSettingsOverride(userId);
      return NoContent();
    }

    database.SaveUserSettingsOverride(userId, _serializationHelperService.SerializeToJson(request.Settings));
    return NoContent();
  }

  /// <summary>
  /// Clears the settings targeted at one user.
  /// </summary>
  /// <param name="userId">The Jellyfin user id.</param>
  /// <returns>No content.</returns>
  [HttpDelete("v1/users/{userId}/settings")]
  [HttpDelete("users/{userId}/settings")]
  [Authorize(Policy = Policies.RequiresElevation)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  public ActionResult ClearUserSettingsOverride([FromRoute, Required] Guid userId)
  {
    StreamyfinPlugin.Instance!.Database.RemoveUserSettingsOverride(userId);
    return NoContent();
  }

  /// <summary>
  /// The settings the calling user actually gets, with the three levels resolved.
  /// </summary>
  /// <returns>The resolved settings.</returns>
  /// <remarks>
  /// Credentials are stripped unless the caller administers the server. That is not
  /// P1.4, which is about retiring the behaviour of <c>GET config</c> and dealing
  /// with what the app does about it. It is this route not being a second way to
  /// read the Seerr key.
  ///
  /// <para>
  /// A caller authenticating with an API key has no user, so there is nothing to
  /// resolve beyond what the server declares for everyone. The key is granted by an
  /// administrator, and Jellyfin's own elevation policy accepts one, so it is treated
  /// as elevated here too rather than as an anonymous caller.
  /// </para>
  /// </remarks>
  [HttpGet("v1/config/resolved")]
  [HttpGet("config/resolved")]
  [Authorize]
  [ProducesResponseType(StatusCodes.Status200OK)]
  public ActionResult GetResolvedSettings()
  {
    var callerId = CallerId;
    var database = StreamyfinPlugin.Instance!.Database;

    var resolved = Resolution.Resolve(
      StreamyfinPlugin.Instance!.Settings.Current.settings,
      database.GetGroupsForUser(callerId),
      database.GetUserSettingsOverride(callerId));

    if (!CallerIsApiKey && !_userManager.IsAdministrator(callerId))
    {
      resolved = SettingsResolver.Redact(resolved);
    }

    return new JsonStringResult(_serializationHelperService.SerializeToJson(resolved));
  }

  // Through the same tolerant read the resolution uses. Nothing validates the JSON
  // on the way in, and a group whose settings cannot be read has to still appear in
  // the list, or an administrator cannot see it to repair it.
  /// <summary>
  /// The configuration the caller is allowed to see, with their levels resolved.
  /// </summary>
  /// <returns>The configuration.</returns>
  private Configuration.Config ConfigForCaller()
  {
    var callerId = CallerId;
    var database = StreamyfinPlugin.Instance!.Database;

    return Resolution.ForCaller(
      StreamyfinPlugin.Instance!.Settings.Current,
      database.GetGroupsForUser(callerId),
      database.GetUserSettingsOverride(callerId),
      CallerIsApiKey || _userManager.IsAdministrator(callerId));
  }

  private SettingsGroupDto ToDto(SettingsGroup group, List<Guid> members) => new()
  {
    Id = group.Id,
    Name = group.Name,
    Priority = group.Priority,
    Settings = Resolution.ReadLevel(group.SettingsJson, $"group {group.Name}"),
    UserIds = members
  };

  // endregion Settings groups
}
