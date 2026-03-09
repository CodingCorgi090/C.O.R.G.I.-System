using System;
using System.IO;
using UnityEngine;

namespace _Game.Scripts.Persistence
{
    public static class PlayerPatternMemoryStore
    {
        private const string FileName = "player-playstyle-memory.json";
        private static PlayerPlaystyleProfile _cachedProfile;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static PlayerPlaystyleProfile LoadOrCreate()
        {
            if (_cachedProfile != null)
            {
                _cachedProfile.EnsureVersion();
                return _cachedProfile;
            }

            try
            {
                if (!File.Exists(FilePath))
                {
                    _cachedProfile = new PlayerPlaystyleProfile();
                    _cachedProfile.EnsureVersion();
                    return _cachedProfile;
                }

                var json = File.ReadAllText(FilePath);
                _cachedProfile = string.IsNullOrWhiteSpace(json)
                    ? new PlayerPlaystyleProfile()
                    : JsonUtility.FromJson<PlayerPlaystyleProfile>(json) ?? new PlayerPlaystyleProfile();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to load player pattern memory from '{FilePath}'. Starting with a fresh profile. {exception.Message}");
                _cachedProfile = new PlayerPlaystyleProfile();
            }

            _cachedProfile.EnsureVersion();
            return _cachedProfile;
        }

        public static void Save(PlayerPlaystyleProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            try
            {
                profile.EnsureVersion();
                _cachedProfile = profile;

                var directoryPath = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var json = JsonUtility.ToJson(profile, true);
                var temporaryFilePath = FilePath + ".tmp";
                File.WriteAllText(temporaryFilePath, json);

                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(temporaryFilePath, FilePath, null);
                    }
                    catch (IOException)
                    {
                        File.Delete(FilePath);
                        File.Move(temporaryFilePath, FilePath);
                    }
                }
                else
                {
                    File.Move(temporaryFilePath, FilePath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to save player pattern memory to '{FilePath}'. {exception.Message}");
            }
        }

        public static void Clear()
        {
            _cachedProfile = new PlayerPlaystyleProfile();
            _cachedProfile.EnsureVersion();

            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to clear player pattern memory at '{FilePath}'. {exception.Message}");
            }
        }
    }
}

