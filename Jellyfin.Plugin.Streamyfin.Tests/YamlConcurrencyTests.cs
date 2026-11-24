using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The controller's <see cref="SerializationHelper"/> is shared by every request, and YamlDotNet
/// 16.0.0 fills a cache that is not safe for concurrent writes the first time its serializer and
/// its deserializer meet a type. After a restart the dashboard asks for <c>config/default</c> and
/// <c>config/yaml</c> as each tab opens, from every browser that has one open, so first calls do
/// arrive together.
/// </summary>
public class YamlConcurrencyTests
{
    private const int Rounds = 150;
    private const int Callers = 8;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="call"/> on <see cref="Callers"/> threads released at the same instant,
    /// on a helper that has never serialized anything, then once more on its own, and returns what
    /// the calls threw. The last call is the one that says whether the helper is broken for good.
    /// </summary>
    private static ConcurrentQueue<Exception> ThrownByFirstCallsTogether(Action<SerializationHelper> call)
    {
        var thrown = new ConcurrentQueue<Exception>();
        for (var round = 0; round < Rounds; round++)
        {
            var helper = new SerializationHelper();
            using var start = new Barrier(Callers);
            var threads = Enumerable.Range(0, Callers).Select(_ => new Thread(() =>
            {
                // A participant that never arrives fails the round instead of holding the others.
                if (!start.SignalAndWait(Patience))
                {
                    thrown.Enqueue(new TimeoutException("A caller waited for the others in vain."));
                    return;
                }

                try
                {
                    call(helper);
                }
                catch (Exception e)
                {
                    thrown.Enqueue(e);
                }
            })
            {
                // A thread stuck for good must not keep the test host from exiting.
                IsBackground = true,
            }).ToList();

            threads.ForEach(thread => thread.Start());
            Assert.All(threads, thread => Assert.True(thread.Join(Patience), "A caller never returned."));

            try
            {
                call(helper);
            }
            catch (Exception e)
            {
                thrown.Enqueue(e);
            }
        }

        return thrown;
    }

    /// <summary>
    /// Fails with the first exception and the count, so a concurrency failure reads as one and an
    /// unrelated break, such as a type YamlDotNet cannot serialize, does not pass for it.
    /// </summary>
    private static void AssertNothingThrown(ConcurrentQueue<Exception> thrown)
    {
        Assert.True(
            thrown.IsEmpty,
            thrown.TryPeek(out var first) ? $"{thrown.Count} call(s) threw, the first {first.GetType().Name}: {first.Message}" : string.Empty);
    }

    /// <summary>
    /// The configuration served as YAML, by requests that arrive together.
    /// </summary>
    [Fact]
    public void FirstSerializationsArrivingTogetherAllSucceed()
    {
        var config = PluginConfiguration.DefaultConfig();

        AssertNothingThrown(ThrownByFirstCallsTogether(helper => helper.SerializeToYaml(config)));
    }

    /// <summary>
    /// YAML saved from two tabs at once, read by a helper that has not read anything yet.
    /// </summary>
    [Fact]
    public void FirstDeserializationsArrivingTogetherAllSucceed()
    {
        var yaml = new SerializationHelper().SerializeToYaml(PluginConfiguration.DefaultConfig());

        AssertNothingThrown(ThrownByFirstCallsTogether(helper => helper.Deserialize<Config>(yaml)));
    }
}
