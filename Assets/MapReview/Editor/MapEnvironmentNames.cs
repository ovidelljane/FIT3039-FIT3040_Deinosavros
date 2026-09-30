using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Deinosavros.MapReview.Editor
{
    public static class MapEnvironmentNames
    {
        private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
        {
            ["Review Background"] = "Background",
            ["Background Chasm floor"] = "CloudSea",
            ["Background Low valley haze"] = "Haze",
            ["Background Fossil spine.010"] = "Spine",
            ["V2 Island continuous fractured bedrock"] = "Island",
            ["Portal - carved solid ring"] = "Portal",
            ["Portal - energy veil"] = "PortalFX",
            ["Portal - recessed luminous inner rim"] = "PortalGlow",
            ["Sanctuary carved approach stair"] = "Stairs",
            ["Sanctuary foundation"] = "Base_01",
            ["V2 Sanctuary upper approach landing"] = "Landing",
            ["V2 Entrance fractured arch and jamb"] = "Arch_01",
            ["V2 Entrance broken opposite jamb"] = "Arch_02",
            ["V2 Entrance corresponding fallen arch section"] = "Arch_01_Fallen",
            ["V2 Entrance north wall root"] = "Wall_01",
            ["V2 Entrance south wall root"] = "Wall_02",
            ["V2 Sanctuary north retaining return"] = "Wall_03",
            ["V2 Sanctuary south retaining return"] = "Wall_04",
            ["V2 Rear continuous colonnade footing"] = "Base_02",
            ["V2 Rear vacant bay foundation"] = "Base_03",
            ["V2 Rear surviving fractured entablature"] = "Beam_01",
            ["V2 Entrance broken order"] = "Column_01",
            ["V2 Entrance broken order matching fallen shaft"] = "Column_01_Fallen",
            ["V2 Sanctuary rear order"] = "Column_06",
            ["V2 Sanctuary rear order matching fallen shaft"] = "Column_06_Fallen"
        };

        // Only known source labels are mapped. Custom names and all gameplay anchors stay intact.
        public static string ShortName(string name)
        {
            if (Labels.TryGetValue(name, out string label)) return label;
            var match = Regex.Match(name, @"^(Paved route|Node terrace|Offering brazier|Brazier flame|Background Distant karst|Background Fossil rib) (\d{2})$");
            if (match.Success)
            {
                string prefix = match.Groups[1].Value switch
                {
                    "Paved route" => "Path", "Node terrace" => "Platform",
                    "Offering brazier" => "Brazier", "Brazier flame" => "Flame",
                    "Background Distant karst" => "Mountain", _ => "Rib"
                };
                return $"{prefix}_{int.Parse(match.Groups[2].Value) + 1:00}";
            }
            match = Regex.Match(name, @"^V2 Rear colonnade order (\d{2})( matching fallen shaft)?$");
            if (match.Success)
                return $"Column_{int.Parse(match.Groups[1].Value) + 2:00}" + (match.Groups[2].Success ? "_Fallen" : "");
            match = Regex.Match(name, @"^V2 Palm (\d{2}) (attached broad pinnae runtime cards|connected petioles and rachises|continuous trunk)$");
            if (match.Success)
            {
                string part = match.Groups[2].Value switch
                {
                    "continuous trunk" => "Trunk", "connected petioles and rachises" => "Stem", _ => "Leaves"
                };
                return $"Palm_{int.Parse(match.Groups[1].Value) + 1:00}_{part}";
            }
            match = Regex.Match(name, @"^V2 (Connected grove broadleaf|Connected low broadleaf patch|Courtyard broadleaf) (\d{2}) (connected petioles|folded blades)$");
            if (match.Success)
            {
                int n = int.Parse(match.Groups[2].Value);
                int id = match.Groups[1].Value switch
                {
                    "Connected grove broadleaf" => n / 5,
                    "Connected low broadleaf patch" => 6 + n / 2,
                    _ => 20 + n
                };
                return $"Bush_{id:00}_" + (match.Groups[3].Value == "folded blades" ? "Leaves" : "Stem");
            }
            match = Regex.Match(name, @"^V2 (Connected grove fern|Courtyard soil pocket|Masonry root understory|Palm root understory) (\d{2})(?: (\d))? (pinnae runtime cards|rooted frond axes)$");
            if (match.Success)
            {
                int n = int.Parse(match.Groups[2].Value);
                int sub = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
                int id = match.Groups[1].Value switch
                {
                    "Connected grove fern" => n,
                    "Courtyard soil pocket" => 29 + n * 5 + sub,
                    "Masonry root understory" => 44 + n,
                    _ => 52 + n * 3 + sub
                };
                return $"Fern_{id:00}_" + (match.Groups[4].Value == "pinnae runtime cards" ? "Leaves" : "Stem");
            }
            return name;
        }

        public static int Apply(GameObject environment, bool recordUndo = false)
        {
            var changes = environment.GetComponentsInChildren<Transform>(true)
                .Where(t => t != environment.transform && ShortName(t.name) != t.name).ToArray();
            var targets = changes.ToDictionary(t => t, t => ShortName(t.name));
            foreach (var item in changes)
                foreach (Transform sibling in item.parent)
                    if (sibling != item && (targets.TryGetValue(sibling, out string name) ? name : sibling.name) == targets[item])
                        throw new InvalidOperationException("Duplicate target label: " + targets[item]);
            if (recordUndo && changes.Length > 0)
                Undo.RecordObjects(changes.Select(t => (Object)t.gameObject).ToArray(), "Simplify Map Names");
            foreach (var item in changes)
            {
                item.name = targets[item];
                if (PrefabUtility.IsPartOfPrefabInstance(item.gameObject))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(item.gameObject);
            }
            return changes.Length;
        }

        [Serializable] private sealed class Change { public string id; public string before; public string after; }
        [Serializable] private sealed class Report
        {
            public int renamed; public int verifiedComponents; public int meshCount;
            public bool saved; public bool priorUnsavedChanges; public bool repeatApplyNoChanges;
            public Change[] changes;
        }

        [MenuItem("Tools/Map Review/Simplify Formal Map Names")]
        public static void SimplifyCurrentScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before renaming.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/Map.unity")
                throw new InvalidOperationException("Open the formal Map scene before renaming.");
            var transforms = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var environment = transforms.Single(t => t.name == "MapEnvironment");
            var components = transforms.SelectMany(t => t.GetComponents<Component>()).Where(c => c != null)
                .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            var poses = transforms.ToDictionary(t => t, t => (parent: t.parent, sibling: t.GetSiblingIndex()));
            var states = transforms.ToDictionary(t => t, t => GameObjectState(t.gameObject));
            var originalNames = transforms.ToDictionary(t => t, t => t.name);
            var background = environment.GetComponentsInChildren<Renderer>(true)
                .ToDictionary(r => r, r => MapReviewCamera.IsBackgroundRenderer(r, environment));
            var changes = environment.GetComponentsInChildren<Transform>(true)
                .Where(t => ShortName(t.name) != t.name)
                .Select(t => new Change { id = GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString(), before = t.name, after = ShortName(t.name) }).ToArray();
            bool wasDirty = scene.isDirty;
            Directory.CreateDirectory(MapEnvironmentLiveSession.Evidence);
            string backup = MapEnvironmentLiveSession.Evidence + "/Map-before-names-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".unity";
            // A save-as-copy captures unsaved user edits without reloading or changing the active scene.
            if (!EditorSceneManager.SaveScene(scene, backup, true))
                throw new IOException("Could not create the pre-rename scene copy.");
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Simplify Map Names");
            bool committed = false;
            try
            {
                int count = Apply(environment.gameObject, true);
                foreach (var pair in components)
                    if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value)
                        throw new InvalidOperationException("Renaming changed component data: " + pair.Key);
                foreach (var t in transforms)
                    if (t.parent != poses[t].parent || t.GetSiblingIndex() != poses[t].sibling || GameObjectState(t.gameObject) != states[t])
                        throw new InvalidOperationException("Renaming changed object state or hierarchy.");
                foreach (var pair in background)
                    if (MapReviewCamera.IsBackgroundRenderer(pair.Key, environment) != pair.Value)
                        throw new InvalidOperationException("Background classification changed.");
                if (Apply(environment.gameObject) != 0)
                    throw new InvalidOperationException("Names are not stable on repeat application.");
                if (count > 0) EditorSceneManager.MarkSceneDirty(scene);
                bool saved = false;
                if (!wasDirty)
                {
                    if (!EditorSceneManager.SaveScene(scene))
                        throw new IOException("Could not save the renamed scene.");
                    saved = committed = true;
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(undoGroup);
                File.WriteAllText(MapEnvironmentLiveSession.Evidence + "/hierarchy-names.json", JsonUtility.ToJson(new Report
                {
                    renamed = count, verifiedComponents = components.Count,
                    meshCount = environment.GetComponentsInChildren<MeshFilter>(true).Length,
                    saved = saved, priorUnsavedChanges = wasDirty, repeatApplyNoChanges = true, changes = changes
                }, true));
            }
            catch
            {
                if (!committed)
                {
                    Undo.FlushUndoRecordObjects();
                    Undo.RevertAllDownToGroup(undoGroup);
                    foreach (var pair in originalNames) if (pair.Key != null) pair.Key.name = pair.Value;
                }
                throw;
            }
        }

        private static string GameObjectState(GameObject item) =>
            Regex.Replace(EditorJsonUtility.ToJson(item), "\"m_Name\":\"(?:\\\\.|[^\"\\\\])*\"", "\"m_Name\":\"\"");
    }
}
