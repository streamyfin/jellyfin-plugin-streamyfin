using System;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The build for Jellyfin 10.11 runs on Jellyfin 12 as well: a server moved to 12 keeps it until
/// the next release (#241). It is compiled against EF Core 9 and runs on 12's EF Core 10, so a
/// method that names what EF Core 10 no longer has fails to compile there. The repair of the
/// group names called <c>ExecuteUpdate</c>, whose argument EF Core 10 changed, and on 12 the
/// plugin did not start: Jellyfin marked it Malfunctioned.
/// </summary>
/// <remarks>
/// The build refuses <c>ExecuteUpdate</c> in the plugin, through <c>BannedSymbols.txt</c>
/// (RS0030), in both of its shapes, since each build sees only its own EF Core's. Loading the
/// entities and saving the changes compiles to the same calls on both.
/// </remarks>
public class EfCoreAcrossLinesTests
{
    /// <summary>
    /// Every <c>ExecuteUpdate</c> and <c>ExecuteUpdateAsync</c> of the EF Core this build runs
    /// against has its entry in the banned list, under the documentation id RS0030 matches: an
    /// entry naming no method of it is ignored without a word, so a mistyped one, or a shape a
    /// later EF Core adds, would let a call through.
    /// </summary>
    [Fact]
    public void TheBuildRefusesEveryExecuteUpdateOfItsEfCore()
    {
        var methods = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is nameof(EntityFrameworkQueryableExtensions.ExecuteUpdate)
                or nameof(EntityFrameworkQueryableExtensions.ExecuteUpdateAsync))
            .ToList();
        var banned = SourceTree.BannedSymbols();

        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Contains(DocumentationId(method), banned));
    }

    /// <summary>
    /// The documentation id of a method, as the compiler writes it and RS0030 reads it.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>Its id, <c>M:</c> and all.</returns>
    private static string DocumentationId(MethodInfo method)
    {
        var arity = method.IsGenericMethodDefinition ? $"``{method.GetGenericArguments().Length}" : string.Empty;
        var parameters = string.Join(",", method.GetParameters().Select(parameter => TypeId(parameter.ParameterType)));
        return $"M:{method.DeclaringType!.FullName}.{method.Name}{arity}({parameters})";
    }

    private static string TypeId(Type type)
    {
        if (type.IsGenericMethodParameter)
        {
            return $"``{type.GenericParameterPosition}";
        }

        if (type.IsGenericTypeParameter)
        {
            return $"`{type.GenericParameterPosition}";
        }

        if (!type.IsGenericType)
        {
            return type.FullName!.Replace('+', '.');
        }

        var definition = type.GetGenericTypeDefinition().FullName!;
        var name = definition[..definition.IndexOf('`', StringComparison.Ordinal)].Replace('+', '.');
        return $"{name}{{{string.Join(",", type.GetGenericArguments().Select(TypeId))}}}";
    }
}
