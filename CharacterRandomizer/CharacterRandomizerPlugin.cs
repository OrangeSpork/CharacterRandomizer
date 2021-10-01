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
using System.Collections.Generic;
using System.IO;
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
        public const string Version = "1.0.0";

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
        public static List<string> CurrentCharacters { get; set; } = new List<string>();

        private static MethodInfo loadFileMethod = AccessTools.Method(typeof(ChaFile), "LoadFile", new Type[] { typeof(string), typeof(int), typeof(bool), typeof(bool) });

        public static float LastReplacementTime { get; set; }
        public static float NextReplacementTime { get; set; } = float.MaxValue;
    
        public CharacterRandomizerPlugin()
        {
            if (Instance != null)
                throw new InvalidOperationException("Singleton only.");

            Instance = this;

            MinimumDelay = Config.Bind("Options", "Minimum Replacement Delay", 10, "Minimum Floor for Replacements, To Keep People from Soft-Locking, adjust at your peril.");
            DefaultSubdirectory = Config.Bind("Options", "Default Subdirectory", "", "Default subdirectory to pull replacement characters from. Default to blank (usual male/female card directory)");
            ReplaceKey = Config.Bind("Hotkeys", "Trigger Character Replacement", new KeyboardShortcut(KeyCode.None), new ConfigDescription("Triggers Character Replacement Manually Using Current Options."));

            var harmony = new Harmony(GUID);
         
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
