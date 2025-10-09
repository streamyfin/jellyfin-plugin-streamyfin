using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.Storage;
using Jellyfin.Plugin.Streamyfin.Storage.Models;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Assert = ICU4N.Impl.Assert;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Run before and after every db test
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CleanupDatabaseBeforeAndAfter: BeforeAfterTestAttribute
{
    private readonly Storage.Database db = new(Directory.GetCurrentDirectory());

    public override void Before(MethodInfo methodUnderTest)
    {
        db.Purge();
    }

    public override void After(MethodInfo methodUnderTest)
    {
        db.Purge();
    }
}

/// <summary>
/// Ensure [Jellyfin.Plugin.Streamyfin.Storage.Database] can properly run transactions as expected
/// </summary>
[CleanupDatabaseBeforeAndAfter]
public class DatabaseTests(ITestOutputHelper output): IDisposable
{
    private readonly Storage.Database db = new(Directory.GetCurrentDirectory());

    /// <summary>
    /// Ensure when adding a device token for a specific device that we delete any previous old token first 
    /// </summary>
    [Fact]
    public void TestAddingDeviceTokenForTheSameDevice()
    {
        var deviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var token = db.AddDeviceToken(
            new DeviceToken
            {
                DeviceId = deviceId,
                Token = "testToken",
                UserId = userId
            }
        );
        
        // Adding a "new" token should update the timestamp
        var updatedToken = db.AddDeviceToken(token);
        var newTokenReference = db.GetDeviceTokenForDeviceId(token.DeviceId);
        
        Assert.Assrt(
            $"Timestamp was updated",
            updatedToken.Timestamp == newTokenReference.Timestamp
        );
        Assert.Assrt(
            $"DeviceId fetched correctly",
            deviceId == newTokenReference.DeviceId
        );
        Assert.Assrt(
            $"UserId fetched correctly",
            userId == newTokenReference.UserId
        );
    }
    
    /// <summary>
    /// Make sure we are actually recording each token 
    /// </summary>
    [Fact]
    public void TestAllTokensPersistSeparately()
    {
        for (int i = 0; i < 5; i++)
        {
            db.AddDeviceToken(
                new DeviceToken
                {
                    DeviceId = Guid.NewGuid(),
                    Token = $"token{i.ToString(CultureInfo.InvariantCulture)}",
                    UserId = Guid.NewGuid()
                }
            );
        }

        var tokens = db.TotalDevicesCount();

        Assert.Assrt(
            $"All tokens persisted",
            tokens == 5
        );
    }

    public void Dispose()
    {
        output.WriteLine($"Deleting database {db.DbFilePath}");
        File.Delete(db.DbFilePath);
    }
}