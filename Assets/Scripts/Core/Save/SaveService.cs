using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Core.Save
{
    /// <summary>
    /// Writes/reads a named save slot as one JSON file under
    /// Application.persistentDataPath. Each ISaveable contributes an
    /// (id, json) entry; on load the entries are dispatched back by SaveId.
    ///
    /// Restore ORDER follows the order of the saveables argument — callers
    /// with dependencies (e.g. "rebuild world before placing the player")
    /// simply pass the list in dependency order.
    /// </summary>
    public static class SaveService
    {
        [Serializable]
        class SaveFile
        {
            public string savedAtUtc;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        class Entry
        {
            public string id;
            public string data;
        }

        public static string GetPath(string slotName) =>
            Path.Combine(Application.persistentDataPath, slotName + ".json");

        public static bool Exists(string slotName) => File.Exists(GetPath(slotName));

        public static void Save(string slotName, IEnumerable<ISaveable> saveables)
        {
            var file = new SaveFile { savedAtUtc = DateTime.UtcNow.ToString("o") };
            foreach (ISaveable s in saveables)
                file.entries.Add(new Entry { id = s.SaveId, data = s.CaptureState() });

            string path = GetPath(slotName);
            File.WriteAllText(path, JsonUtility.ToJson(file, true));
            Debug.Log($"[SaveService] {file.entries.Count} entries saved -> {path}");
        }

        /// <returns>False if the slot file does not exist or cannot be parsed.</returns>
        public static bool Load(string slotName, IEnumerable<ISaveable> saveables)
        {
            string path = GetPath(slotName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveService] No save file at {path}.");
                return false;
            }

            SaveFile file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
            if (file == null || file.entries == null)
            {
                Debug.LogError($"[SaveService] Could not parse save file {path}.");
                return false;
            }

            var byId = new Dictionary<string, string>();
            foreach (Entry e in file.entries)
                byId[e.id] = e.data;

            int restored = 0;
            foreach (ISaveable s in saveables)   // caller's order = dependency order
            {
                if (byId.TryGetValue(s.SaveId, out string data))
                {
                    s.RestoreState(data);
                    restored++;
                }
                else
                {
                    Debug.LogWarning($"[SaveService] Save file has no entry for '{s.SaveId}' — skipped.");
                }
            }

            Debug.Log($"[SaveService] {restored} entries restored from {path} (saved {file.savedAtUtc}).");
            return true;
        }
    }
}
