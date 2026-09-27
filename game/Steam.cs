using Godot;

namespace FrontPageFoundry;

/// <summary>
/// The Steam achievement sink, to be filled in once the app has an id (docs/STEAM.md). Until a Steamworks
/// binding (GodotSteam C# or Steamworks.NET) is added to the project this only says what it would do,
/// and <see cref="NoSteam"/> keeps the local record. Wiring: construct it in <c>Main._Ready</c> when
/// <c>steam_appid.txt</c> is present and pass it to <see cref="Achievements"/>.
/// </summary>
public sealed class SteamAchievements : IAchievementSink
{
    public void Unlock(string id)
    {
        // With the binding in place:
        //   SteamUserStats.SetAchievement(id);
        //   SteamUserStats.StoreStats();
        GD.Print($"steam: achievement {id} (no Steamworks binding yet)");
    }
}
