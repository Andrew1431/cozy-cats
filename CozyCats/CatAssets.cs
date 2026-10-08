using System.IO;
using System.Linq;
using UnityEngine;

namespace CozyCats;

internal static class CatAssets
{
    public static GameObject CatModel;
    public static AudioClip[] Meows = new AudioClip[0];
    public static AudioClip[] Purrs = new AudioClip[0];

    public static bool Load(string path)
    {
        if (!File.Exists(path)) return false;
        var bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null) return false;

        CatModel = bundle.LoadAsset<GameObject>("CatModel");
        var clips = bundle.LoadAllAssets<AudioClip>();
        Meows = clips.Where(c => c.name.StartsWith("meow")).ToArray();
        Purrs = clips.Where(c => c.name.StartsWith("purr")).ToArray();
        Plugin.Log.LogInfo($"Loaded cat model: {CatModel != null}, {Meows.Length} meows, {Purrs.Length} purrs");
        return CatModel != null;
    }
}
