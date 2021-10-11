using AIChara;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Studio;
using KKAPI.Studio.SaveLoad;
using KKAPI.Studio.UI;
using KKAPI.Utilities;
using Manager;
using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CharacterRandomizer
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInDependency(KoikatuAPI.GUID, KoikatuAPI.VersionConst)]
    [BepInProcess("StudioNEOV2")]
    public class CharacterRandomizerPlugin : BaseUnityPlugin
    {

        public const string GUID = "orange.spork.characterrandomizer";
        public const string PluginName = "Character Randomizer";
        public const string Version = "1.1.5";

        public static CharacterRandomizerPlugin Instance { get; set; }

        internal BepInEx.Logging.ManualLogSource Log => Logger;

        public ToolbarToggle StudioGUIToolbarToggle { get; set; }

        // Config
        public static ConfigEntry<int> MinimumDelay { get; set; }
        public static ConfigEntry<KeyboardShortcut> ReplaceKey { get; set; }
        public static ConfigEntry<string> DefaultSubdirectory { get; set; }        

        // vars
        internal static DirectoryInfo FemaleBaseDir = new DirectoryInfo(UserData.Path + "chara/female");
        internal static DirectoryInfo MaleBaseDir = new DirectoryInfo(UserData.Path + "chara/male");

        public static List<ChaFileInfo> FemaleCharaList { get; set; }
        public static List<ChaFileInfo> MaleCharaList { get; set; }

        public static Dictionary<int, string> CurrentFemaleCharacters { get; set; } = new Dictionary<int, string>();
        public static Dictionary<int, string> RotatedFemaleCharacters { get; set; } = new Dictionary<int, string>();
        public static Dictionary<int, string> CurrentMaleCharacters { get; set; } = new Dictionary<int, string>();
        public static Dictionary<int, string> RotatedMaleCharacters { get; set; } = new Dictionary<int, string>();

        private static MethodInfo loadFileMethod = AccessTools.Method(typeof(ChaFile), "LoadFile", new Type[] { typeof(string), typeof(int), typeof(bool), typeof(bool) });

        public static float LastReplacementTime { get; set; }
        public static float NextReplacementTime { get; set; } = 0f;
        public static string LastLoadedFile { get; set; }
        public static void ConvertCharaFilePathMonitor(string __result)
        {
            LastLoadedFile = __result;
        }

        public static List<OCIFolder> FolderRequestFlags { get; set; } = new List<OCIFolder>();

        public CharacterRandomizerPlugin()
        {
            if (Instance != null)
                throw new InvalidOperationException("Singleton only.");

            Instance = this;

            MinimumDelay = Config.Bind("Options", "Minimum Replacement Delay", 10, "Minimum Floor for Replacements, To Keep People from Soft-Locking, adjust at your peril.");
            DefaultSubdirectory = Config.Bind("Options", "Default Subdirectory", "", "Default subdirectory to pull replacement characters from. Default to blank (usual male/female card directory)");
            ReplaceKey = Config.Bind("Hotkeys", "Trigger Character Replacement", new KeyboardShortcut(KeyCode.None), new ConfigDescription("Triggers Character Replacement Manually Using Current Options."));

            var harmony = new Harmony(GUID);

            harmony.Patch(AccessTools.Method(typeof(ChaFileControl), "ConvertCharaFilePath"), null, new HarmonyMethod(typeof(CharacterRandomizerPlugin), "ConvertCharaFilePathMonitor"));
            harmony.Patch(AccessTools.Method(typeof(AddObjectFolder), "Load", new Type[] { typeof(OIFolderInfo), typeof(ObjectCtrlInfo), typeof(TreeNodeObject), typeof(bool), typeof(int) }), null, new HarmonyMethod(typeof(CharacterRandomizerPlugin), "AddFolderMonitor"));
         
#if DEBUG
            Log.LogInfo("Character Randomizer Loaded.");
#endif
        }

        public void Update()
        {
            if (ReplaceKey.Value.IsUp())
            {
                foreach (OCIChar character in StudioAPI.GetSelectedCharacters())
                {
                    CharacterRandomizerCharaController randomizer = character.GetChaControl().gameObject.GetComponent<CharacterRandomizerCharaController>();
                    if (randomizer != null)
                    {
                        randomizer.ReplaceCharacter();
                    }
                }
            }

            if (NextReplacementTime > 0 && Time.time > NextReplacementTime)
            {
                if (CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances.Cast<CharacterRandomizerCharaController>().Any(cont => cont.Running))
                    ReplaceAll(false);
                else
                    NextReplacementTime = 0;
                
            }

            CheckForRequestedReplacementsViaFolder();

        }

        public void OnEnable()
        {
            StartCoroutine(ScanForFolderFlagsCo());
        }

        
        private IEnumerator ScanForFolderFlagsCo()
        {
            yield return new WaitUntil(() => Studio.Studio.Instance != null && Studio.Studio.Instance.dicObjectCtrl != null);
            while (this.enabled)
            {
                ScanForFolderFlags();
                yield return new WaitForSeconds(10);
            }
        }

        public void ScanForFolderFlags()
        {
            FolderRequestFlags.Clear();
            foreach (ObjectCtrlInfo ctrlInfo in Studio.Studio.Instance.dicObjectCtrl.Values)
            {
                if (ctrlInfo.GetType() == typeof(OCIFolder))
                {
                    OCIFolder folder = (OCIFolder)ctrlInfo;
                    if (folder.name != null && folder.name.ToUpper().StartsWith("-RNG"))
                    {
                        FolderRequestFlags.Add(folder);
                    }
                }
            }
        }

        private void CheckForRequestedReplacementsViaFolder()
        {
            bool rescan = false;
            foreach (OCIFolder folder in FolderRequestFlags)
            {
                if (folder == null || folder.objectItem == null)
                {
                    rescan = true;
                    continue;
                }

                if (folder.name != null && folder.name.ToUpper().StartsWith("-RNG") && folder.treeNodeObject.visible)
                {
                    if (folder.name.EndsWith("ALL"))
                    {
    #if DEBUG
                        Log.LogInfo($"Replace ALL via folder requested.");
    #endif
                        CharacterRandomizerPlugin.ReplaceAll(true);                        
                    }
                    else
                    {
                        try
                        {
                            string charRequest = folder.name.Substring(folder.name.LastIndexOf(":") + 1);
                            char sex = charRequest.ToUpper()[0];
                            int index = int.Parse(charRequest.Substring(1));

    #if DEBUG
                            Log.LogInfo($"Replace {sex}:{index} via folder requested.");
    #endif

                            foreach (CharacterRandomizerCharaController randomizer in CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances)
                            {
                                if (randomizer.ChaControl.sex == 0 && sex == 'M' && randomizer.RotationOrder == index)
                                {
                                    randomizer.ReplaceCharacter();
                                }
                                else if (randomizer.ChaControl.sex == 1 && sex == 'F' && randomizer.RotationOrder == index)
                                {
                                    randomizer.ReplaceCharacter();
                                }
                            }
                        }
                        catch
                        {
                            Log.LogWarning($"Malformed Character Randomizer Folder Request: {folder.name}");
                        }
                    }
                    folder.treeNodeObject.SetVisible(false);
                }
            }

            if (rescan)
                ScanForFolderFlags();
        }

        public void Start()
        {
            RefreshLists();

            StudioSaveLoadApi.RegisterExtraBehaviour<CharacterRandomizerSceneController>(GUID);
            CharacterApi.RegisterExtraBehaviour<CharacterRandomizerCharaController>(GUID);

            gameObject.AddComponent<CharacterRandomizerStudioGUI>();

            Texture2D gIconTex = new Texture2D(32, 32);
            byte[] texData = ResourceUtils.GetEmbeddedResource("chara_random_studio.png");
            ImageConversion.LoadImage(gIconTex, texData);
            StudioGUIToolbarToggle = KKAPI.Studio.UI.CustomToolbarButtons.AddLeftToolbarToggle(gIconTex, false, active => {
                CharacterRandomizerStudioGUI.Instance.enabled = active;
            }); 
        }

        public static void ReplaceAll(bool force)
        {
            RotatedFemaleCharacters.Clear();
            CharacterRandomizerCharaController.RotationMode femaleRotationMode = RotationModeForSex(1);
            if (femaleRotationMode != CharacterRandomizerCharaController.RotationMode.NONE)
                CalculateRotations(femaleRotationMode, 1);

            RotatedMaleCharacters.Clear();
            CharacterRandomizerCharaController.RotationMode maleRotationMode = RotationModeForSex(0);
            if (maleRotationMode != CharacterRandomizerCharaController.RotationMode.NONE)
                CalculateRotations(maleRotationMode, 0);

#if DEBUG
            Instance.Log.LogInfo($"Rotation Calculation:");
            LogCurrentCharacterRegistry();
            LogRotationCharacterRegistry();
#endif

            foreach (CharacterRandomizerCharaController randomizer in CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances)
            {
                if (randomizer.UseSyncedTime && (force || randomizer.Running))
                    randomizer.ReplaceCharacter();
            }
            
        }

        private static CharacterRandomizerCharaController.RotationMode RotationModeForSex(int sex)
        {
            foreach (CharacterRandomizerCharaController controller in CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances)
            {
                if (controller.ChaControl.sex == sex)
                    return controller.Rotation;
            }
            return CharacterRandomizerCharaController.RotationMode.NONE;
        }

        private static void CalculateRotations(CharacterRandomizerCharaController.RotationMode rotationMode, int sex)
        {
            int maxPosition = sex == 0 ? CharacterRandomizerPlugin.CurrentMaleCharacters.Keys.Max() : CharacterRandomizerPlugin.CurrentFemaleCharacters.Keys.Max();
           
            if (rotationMode == CharacterRandomizerCharaController.RotationMode.FWD || rotationMode == CharacterRandomizerCharaController.RotationMode.WRAP_FWD)
            {
                foreach (int position in sex == 0 ? CurrentMaleCharacters.Keys : CurrentFemaleCharacters.Keys)
                {
                    if (!ControllerForPosition(position, sex).UseSyncedTime)
                        continue;

                    bool found = false;
                    int newPosition = position + 1;
                    while (newPosition <= maxPosition)
                    {
                        if ((sex == 0 ? CurrentMaleCharacters.ContainsKey(newPosition) : CurrentFemaleCharacters.ContainsKey(newPosition)) && ControllerForPosition(newPosition, sex).UseSyncedTime)
                        {
                            found = true;
                            break;
                        }

                        newPosition++;
                    }                    
                    if (!found && newPosition > maxPosition && rotationMode == CharacterRandomizerCharaController.RotationMode.WRAP_FWD)
                        newPosition = 1;
                    else if (!found)
                        continue;

                    if (sex == 0)
                        RotatedMaleCharacters.Add(newPosition, CurrentMaleCharacters[position]);
                    else
                        RotatedFemaleCharacters.Add(newPosition, CurrentFemaleCharacters[position]);
                }
            }
            else
            {
                foreach (int position in sex == 0 ? CurrentMaleCharacters.Keys : CurrentFemaleCharacters.Keys)
                {
                    if (!ControllerForPosition(position, sex).UseSyncedTime)
                        continue;

                    bool found = false;
                    int newPosition = position - 1;
                    while (newPosition > 0)
                    {
                        if ((sex == 0 ? CurrentMaleCharacters.ContainsKey(newPosition) : CurrentFemaleCharacters.ContainsKey(newPosition)) && ControllerForPosition(newPosition, sex).UseSyncedTime)
                        {
                            found = true;
                            break;
                        }

                        newPosition--;
                    }
                    if (!found && newPosition == 0 && rotationMode == CharacterRandomizerCharaController.RotationMode.WRAP_REV)
                        newPosition = maxPosition;
                    else if (!found)
                        continue;

                    if (sex == 0)
                        RotatedMaleCharacters.Add(newPosition, CurrentMaleCharacters[position]);
                    else
                        RotatedFemaleCharacters.Add(newPosition, CurrentFemaleCharacters[position]);
                }
            }
        }

        public void RefreshLists()
        {
            FemaleCharaList = RetrieveCharaLists(true);
            MaleCharaList = RetrieveCharaLists(false);
        }

        public List<ChaFileInfo> RetrieveCharaLists(bool female)
        {
            DirectoryInfo directory = female ? FemaleBaseDir : MaleBaseDir;
#if DEBUG
            Log.LogInfo($"Scanning Directory: {directory} Load Invoker: {loadFileMethod} Lang: {0}");
#endif
            FileInfo[] fileNames = directory.GetFiles("*.png", SearchOption.AllDirectories);


            ExtensibleSaveFormat.ExtendedSave.LoadEventsEnabled = false;
            List<ChaFileInfo> chaList = new List<ChaFileInfo>();
            Array.ForEach(fileNames, file =>
            {
                try
                {
                    ChaFile chaFile = new ChaFile();
                    bool success = (bool)loadFileMethod.Invoke(chaFile, new object[] { file.FullName, 0, true, true });
                    if (success)
                    {
                        ChaFileInfo chaInfo = new ChaFileInfo(file.FullName, chaFile?.parameter?.fullname, file.LastWriteTime);
                        chaList.Add(chaInfo);
                    }
                }
                catch (Exception e)
                {
#if DEBUG
                    Log.LogInfo($"Couldn't Load Card {e.Message}");
#endif
                }            
            });
            ExtensibleSaveFormat.ExtendedSave.LoadEventsEnabled = true;
            return chaList;
        }

        public static void LogCurrentCharacterRegistry()
        {
#if DEBUG
            Instance.Log.LogInfo($"Current Male Characters:");
            foreach (int position in CharacterRandomizerPlugin.CurrentMaleCharacters.Keys)
                Instance.Log.LogInfo($"{position}: {ControllerForPosition(position, 0)?.ChaControl.fileParam.fullname} {CharacterRandomizerPlugin.CurrentMaleCharacters[position]}");
            Instance.Log.LogInfo($"Current Female Characters:");
            foreach (int position in CharacterRandomizerPlugin.CurrentFemaleCharacters.Keys)
                Instance.Log.LogInfo($"{position}: {ControllerForPosition(position, 1)?.ChaControl.fileParam.fullname} {CharacterRandomizerPlugin.CurrentFemaleCharacters[position]}");
#endif
        }

        public static void LogRotationCharacterRegistry()
        {
#if DEBUG
            Instance.Log.LogInfo($"Rotated Male Characters:");
            foreach (int position in CharacterRandomizerPlugin.RotatedMaleCharacters.Keys)
                Instance.Log.LogInfo($"{position}: {CharacterRandomizerPlugin.RotatedMaleCharacters[position]}");
            Instance.Log.LogInfo($"Rotated Female Characters:");
            foreach (int position in CharacterRandomizerPlugin.RotatedFemaleCharacters.Keys)
                Instance.Log.LogInfo($"{position}: {CharacterRandomizerPlugin.RotatedFemaleCharacters[position]}");
#endif
        }

        public static void AddFolderMonitor()
        {
            CharacterRandomizerPlugin.Instance.ScanForFolderFlags();
        }
        public static CharacterRandomizerCharaController ControllerForPosition(int position, int sex)
        {
            foreach (CharacterRandomizerCharaController controller in CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances)
            {
                if (controller.RotationOrder == position && controller.ChaControl.sex == sex)
                    return controller;
            }
            return null;
        }

        public struct ChaFileInfo
        {
            public string fileName;
            public string charaName;
            public DateTime lastUpdated;

            public ChaFileInfo(string fileName, string charaName, DateTime lastUpdated)
            {
                this.fileName = fileName;
                this.charaName = charaName;
                this.lastUpdated = lastUpdated;
            }

            public override string ToString()
            {
                return $"{fileName} {charaName} {lastUpdated}";
            }
        }

    }
}
