using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Streamyfin.Injection;

/// <summary>
/// Tells the File Transformation plugin, if it is installed, to run
/// <see cref="DrawerLogoPatch"/> over the web client's entry point.
/// </summary>
/// <remarks>
/// A scheduled task on a startup trigger rather than plugin construction, because the
/// other plugin has to be loaded before it can be found, and plugin load order is not
/// something either plugin controls. Jellyfin discovers this by the interface, so
/// nothing registers it.
///
/// <para>
/// File Transformation is not in the official catalogue and most servers will not have
/// it. Its absence is the normal case and costs nothing: the plugin keeps its Material
/// glyph and this logs once at debug.
/// </para>
/// </remarks>
public class DrawerLogoRegistration : IScheduledTask
{
    // Fixed, so a restart replaces the registration rather than adding another.
    private const string TransformationId = "2f6c1a94-8e33-4d7b-9c05-a1b7e2d4f680";
    private const string FileTransformationAssemblyMarker = ".FileTransformation";
    private const string PluginInterfaceType = "Jellyfin.Plugin.FileTransformation.PluginInterface";
    private const string RegisterMethod = "RegisterTransformation";

    private readonly ILogger<DrawerLogoRegistration> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawerLogoRegistration"/> class.
    /// </summary>
    /// <param name="loggerFactory">Logger factory.</param>
    public DrawerLogoRegistration(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _logger = loggerFactory.CreateLogger<DrawerLogoRegistration>();
    }

    /// <inheritdoc />
    public string Name => "Streamyfin drawer logo";

    /// <inheritdoc />
    public string Key => "Jellyfin.Plugin.Streamyfin.DrawerLogo";

    /// <inheritdoc />
    public string Description => "Registers the dashboard drawer logo with the File Transformation plugin, when it is installed.";

    /// <inheritdoc />
    public string Category => "Startup Services";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
        [new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger }];

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var pluginInterface = FindPluginInterface();

        if (pluginInterface is null)
        {
            _logger.LogDebug(
                "File Transformation is not installed, so the drawer keeps the Material icon");
            return Task.CompletedTask;
        }

        var payload = new JObject
        {
            ["id"] = TransformationId,
            ["fileNamePattern"] = "index.html",
            ["callbackAssembly"] = GetType().Assembly.FullName,
            ["callbackClass"] = typeof(DrawerLogoPatch).FullName,
            ["callbackMethod"] = nameof(DrawerLogoPatch.IndexHtml)
        };

        var register = pluginInterface.GetMethod(RegisterMethod);

        if (register is null)
        {
            // The plugin is installed but does not expose what its README documents,
            // which means it changed under us. Worth a warning rather than the silence
            // of a null conditional, since the outcome looks exactly like success.
            _logger.LogWarning(
                "File Transformation is installed but has no {Method}, so the drawer keeps the Material icon",
                RegisterMethod);
            return Task.CompletedTask;
        }

        register.Invoke(null, [payload]);
        _logger.LogInformation("Registered the drawer logo with File Transformation");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Finds the other plugin's entry point across every load context.
    /// </summary>
    /// <returns>The type, or <c>null</c> when the plugin is not installed.</returns>
    /// <remarks>
    /// By reflection because Jellyfin loads each plugin into its own load context, so a
    /// direct reference would resolve to a type the other plugin does not recognise as
    /// its own. This is the idiom its README documents.
    ///
    /// <para>
    /// The interface is the same on both lines. Version 3.0 rewrote the plugin around an
    /// <c>IStartupFilter</c> middleware for Jellyfin 12 and dropped Harmony, but
    /// <c>RegisterTransformation</c> took no part in that: it only gained a
    /// <c>RemoveTransformation</c> sibling. So this needs no <c>Compat</c> entry.
    /// </para>
    /// </remarks>
    private static Type? FindPluginInterface()
    {
        foreach (var context in AssemblyLoadContext.All)
        {
            foreach (var assembly in context.Assemblies)
            {
                if (assembly.FullName?.Contains(FileTransformationAssemblyMarker, StringComparison.Ordinal) != true)
                {
                    continue;
                }

                var type = assembly.GetType(PluginInterfaceType);
                if (type is not null)
                {
                    return type;
                }
            }
        }

        return null;
    }
}
