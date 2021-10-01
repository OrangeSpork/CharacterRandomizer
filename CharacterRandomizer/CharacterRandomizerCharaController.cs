using BepInEx.Logging;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Studio;
using KKAPI.Studio.SaveLoad;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CharacterRandomizer
{
    public class CharacterRandomizerCharaController : CharaCustomFunctionController
    {

        // Load State
        private bool loaded = false;
        public bool Loaded
        {
            get { return loaded;  }
            set { loaded = value;  }
        }

        // Settings

        private bool running = false;
        public bool Running
        {
            get { return running; }
            set {
                if (!running && value)
                    ScheduleNextReplacement();

                running = value;
            }
        }

        private ReplacementMode charReplacementMode = ReplacementMode.RANDOM;
        public ReplacementMode CharReplacementMode
        {
            get { return charReplacementMode; }
            set { charReplacementMode = value;  }
        }

        private int baseDelaySeconds = 60;
        public int BaseDelaySeconds
        {
            get { return baseDelaySeconds; }
            set { baseDelaySeconds = value; }
        }

        private int delayVarianceRange = 30;
        public int DelayVarianceRange
        {
            get { return delayVarianceRange; }
            set { delayVarianceRange = value;  }
        }

        private string subdirectory = "";
        public string Subdirectory
        { 
            get { return subdirectory; }
            set { subdirectory = value; }
        }

        private bool includeChildDirectories = false;
        public bool IncludeChildDirectories
        {
            get { return includeChildDirectories; }
            set { includeChildDirectories = value; }
        }

        private string namePattern = ".*";
        public string NamePattern
        {
            get { return namePattern; }
            set { namePattern = value; }
        }

        private bool useSyncedTime = true;
        public bool UseSyncedTime
        {
            get { return useSyncedTime; }
            set { useSyncedTime = value; }
        }

        private bool fullAsync = false;
        public bool FullAsync
        {
            get { return fullAsync; }
            set { fullAsync = value; }
        }

        private bool noDupes = true;
        public bool NoDupes
        {
            get { return noDupes; }
            set { noDupes = value; }
        }

        private string outfitFile;
        public string OutfitFile
        {
            get { return outfitFile; }
            set { outfitFile = value; }
        }

        private bool preserveOutfit = false;
        public bool PreserveOutfit
        {
            get { return preserveOutfit; }
            set { preserveOutfit = value; }
        }

        // vars
        private float lastReplacementTime = 0f;
        private float nextReplacementTime = 0f;

        private string lastReplacementFile = "";

        private ManualLogSource log => CharacterRandomizerPlugin.Instance.Log;
        private static MethodInfo onCoordinateBeingSavedMethod = AccessTools.Method(typeof(CharacterApi), "OnCoordinateBeingSaved");

        protected override void Start()
        {
            base.Start();            
            if (string.IsNullOrEmpty(subdirectory))
                subdirectory = string.IsNullOrEmpty(CharacterRandomizerPlugin.DefaultSubdirectory.Value) ? "" : CharacterRandomizerPlugin.DefaultSubdirectory.Value;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (running)
                ScheduleNextReplacement();
        }

        protected void OnDisable()
        {
            
        }

        protected override void OnDestroy()
        {
            CharacterRandomizerPlugin.CurrentCharacters.Remove(lastReplacementFile);
            base.OnDestroy();            
        }

        protected override void Update()
        {
            base.Update();
            if (this.running &&  !useSyncedTime && Time.time > nextReplacementTime)
            {
                // Time to do replacement
                if (fullAsync)
                    StartCoroutine(ReplaceCharacterAsync());
                else
                    ReplaceCharacter();
            }
            else if (this.running && useSyncedTime && Time.time > CharacterRandomizerPlugin.NextReplacementTime)
            {
                // Time to do replacement
                if (fullAsync)
                    StartCoroutine(ReplaceCharacterAsync());
                else
                    ReplaceCharacter();
            }
        }

        public IEnumerator ReplaceCharacterAsync()
        {
            yield return new WaitForEndOfFrame();
            ReplaceCharacter();
        }

        public void ReplaceCharacter()
        {
            StartCoroutine(DoReplaceCharacter());
        }

        public IEnumerator DoReplaceCharacter()
        { 

            CharacterRandomizerPlugin.ChaFileInfo replacementChaInfo = PickReplacementCharacter();
#if DEBUG
            log.LogInfo($"Replacing: {ChaControl.fileParam.fullname}\nLast File:{lastReplacementFile}\nPicked Replacement: {replacementChaInfo}");
#endif
            if (string.IsNullOrEmpty(replacementChaInfo.fileName))
                yield break;

            if (!UseSyncedTime)
                ScheduleNextReplacement();
            else
                StartCoroutine(ScheduleNextReplacementAsync());

            bool success = true;
            int i = 0;
            while (success)
            {
                try
                {
                    ChaControl.SetAccessoryState(i, true);
                    i++;
                }
                catch (Exception e)
                {
#if DEBUG
                    log.LogInfo($"Last Accessory Index {i}");
#endif
                    success = false;
                }
            }

            int neckState = ChaControl.neckLookCtrl.ptnNo;
            int gazeState = ChaControl.eyeLookCtrl.ptnNo;

            CharacterRandomizerPlugin.CurrentCharacters.Remove(lastReplacementFile);

            string fileName = "";
            if (preserveOutfit)
            {
                fileName = ChaControl.gameObject.GetInstanceID() + "-randomizer.png";
                ChaControl.nowCoordinate.SaveFile(Path.Combine(UserData.Path, "coordinate", (ChaControl.sex == 0 ? "male" : "female"), fileName), 0);

                CharaCustomFunctionController[] controllers = ChaControl.gameObject.GetComponents<CharaCustomFunctionController>();

                // Need to cheat to make KKAPI behave with saving a coordinate from Studio
                foreach (CharaCustomFunctionController controller in controllers)
                {
                    AccessTools.Field(controller.GetType(), "_wasLoaded").SetValue(controller, true);
#if DEBUG
                    log.LogInfo($"Marking wasLoaded on {controller.name} {controller.GetType().FullName}");
#endif
                }
                onCoordinateBeingSavedMethod.Invoke(null, new object[] { ChaControl, ChaControl.nowCoordinate });                
                yield return null;
                yield return null;
                yield return null;
            }

            CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).MaintainState = true;
            ChaControl.GetOCIChar().ChangeChara(replacementChaInfo.fileName);
            CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).MaintainState = false;

            CharacterRandomizerPlugin.CurrentCharacters.Add(Path.GetFileName(replacementChaInfo.fileName));            

            if (preserveOutfit)
            {
                yield return StartCoroutine(DoClothingLoad(fileName));
            }
            else if (!string.IsNullOrEmpty(outfitFile) && File.Exists(Path.Combine(UserData.Path, "coordinate", (ChaControl.sex == 0 ? "male" : "female"), outfitFile)))
            {
                yield return StartCoroutine(DoClothingLoad(outfitFile));
            }
            else if (!string.IsNullOrEmpty(OutfitFile))
            {
                log.LogWarning($"Specified Replacement Outfit File Not Found: {outfitFile}");
                log.LogMessage($"Specified Replacement Outfit File Not Found: {outfitFile}");
            }

            yield return StartCoroutine(TickleClothesState());
            ChaControl.GetOCIChar().ChangeLookNeckPtn(neckState);
            ChaControl.GetOCIChar().ChangeLookEyesPtn(gazeState);

            lastReplacementTime = Time.time;
            lastReplacementFile = replacementChaInfo.fileName;

#if DEBUG
            log.LogInfo($"Setting Last File {lastReplacementFile}");
#endif


        }

        private IEnumerator DoClothingLoad(string fileName)
        {
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            yield return null;

            if (preserveOutfit)
            {
                ChaControl.GetOCIChar().LoadClothesFile(fileName);
                yield return null;
                yield return null;
                File.Delete(Path.Combine(UserData.Path, "coordinate", (ChaControl.sex == 0 ? "male" : "female"), fileName));                
            }
            else
                ChaControl.GetOCIChar().LoadClothesFile(fileName);
            yield return StartCoroutine(TickleClothesState());

        }

        private IEnumerator TickleClothesState()
        {
            yield return null;
            yield return null;
            ChaControl.GetOCIChar().SetClothesState(0, ChaControl.GetOCIChar().charFileStatus.clothesState[0]);
        }

        private IEnumerator ScheduleNextReplacementAsync()
        {
            yield return new WaitForEndOfFrame();
            ScheduleNextReplacement();
            if (useSyncedTime)
            {
                CharacterRandomizerPlugin.LastReplacementTime = Time.time;
            }
        }

        public float NextTime
        {
            get
            {
                if (useSyncedTime)
                    return CharacterRandomizerPlugin.NextReplacementTime;
                else
                    return nextReplacementTime;
            }
        }

        public void ScheduleNextReplacement(bool force = false)
        {
            if (useSyncedTime)
            {
                if (CharacterRandomizerPlugin.NextReplacementTime <= Time.time || force)
                {
                    CharacterRandomizerPlugin.NextReplacementTime = Time.time + CharacterRandomizerPlugin.MinimumDelay.Value + baseDelaySeconds + (UnityEngine.Random.Range(0, delayVarianceRange));                    
                }
                nextReplacementTime = CharacterRandomizerPlugin.NextReplacementTime;
#if DEBUG
                log.LogInfo($"Using Sync'd Next Character Replacement Last {CharacterRandomizerPlugin.LastReplacementTime} Current {Time.time} Next {CharacterRandomizerPlugin.NextReplacementTime}");
#endif

            }
            else
            {
                nextReplacementTime = Time.time + CharacterRandomizerPlugin.MinimumDelay.Value + baseDelaySeconds + (UnityEngine.Random.Range(0, delayVarianceRange));

#if DEBUG
                log.LogInfo($"Scheduling Next Character Replacement Last {lastReplacementTime} Current {Time.time} Next {nextReplacementTime}");
#endif
            }
        }

        private CharacterRandomizerPlugin.ChaFileInfo PickReplacementCharacter()
        {
            try
            {
                List<CharacterRandomizerPlugin.ChaFileInfo> files = new List<CharacterRandomizerPlugin.ChaFileInfo>(ChaControl.sex == 1 ? CharacterRandomizerPlugin.FemaleCharaList : CharacterRandomizerPlugin.MaleCharaList);
                Dictionary<string, bool> desiredPaths = new Dictionary<string, bool>();
                foreach (string subdir in subdirectory.Split(new char[] { '|' }, StringSplitOptions.None))
                {
                    try
                    {
                        string matchSubDir = subdir;
                        bool includeChildrenOfSubdir = false;
                        if (subdir.EndsWith("*"))
                        {
                            includeChildrenOfSubdir = true;
                            matchSubDir = subdir.TrimEnd(new char[] { '*' });
                        }

                        string desiredPath = Path.Combine((ChaControl.sex == 1 ? CharacterRandomizerPlugin.FemaleBaseDir : CharacterRandomizerPlugin.MaleBaseDir).FullName, matchSubDir);
                        log.LogInfo($"Including Replacement Characters From Directory {desiredPath} {(includeChildrenOfSubdir || includeChildDirectories ? " and child directories" : "")}");
                        desiredPaths[desiredPath] = includeChildrenOfSubdir;
                    }
                    catch (Exception)
                    {
                        log.LogWarning($"Ignoring malformed subdirectory {subdir}");
                    }
                }


                files = files.FindAll(fi =>
                {
                    return desiredPaths.Keys.Where(path =>
                    {
                        if (!includeChildDirectories && !desiredPaths[path])
                            return path == Path.GetDirectoryName(fi.fileName);
                        else
                            return fi.fileName.StartsWith(path);
                    }).Count() > 0;

                });

                if (noDupes)
                {
                    files.RemoveAll(fi =>
                    {
                        return CharacterRandomizerPlugin.CurrentCharacters.Contains(fi.fileName);
                    });
                }

                if (!string.IsNullOrWhiteSpace(namePattern))
                {
                    Regex nameRegex = new Regex(namePattern);
                    files = files.FindAll(fi => nameRegex.IsMatch(fi.charaName));
                    log.LogInfo($"Filtering Replacement Characters by Pattern {namePattern} Matches{(noDupes ? " After Removing Potential Dupes:" : ":")} {files.Count}");
                }

                if (files.Count == 0)
                {                    
                    log.LogWarning($"Cannot Replace Character, No Alternatives Available");
                    log.LogMessage($"Cannot Replace Character, No Available Matches");
                    return new CharacterRandomizerPlugin.ChaFileInfo(null, null, DateTime.Now);
                }

                if (charReplacementMode == ReplacementMode.RANDOM)
                {
                    CharacterRandomizerPlugin.ChaFileInfo replacement = files[UnityEngine.Random.Range(0, files.Count - 1)];

                    if (string.Equals(replacement.fileName, lastReplacementFile) && files.Count == 1)
                    {
                        log.LogWarning($"Cannot Replace Character, No Alternatives Available");
                        log.LogMessage($"Cannot Replace Character, No Available Matches");
                        return new CharacterRandomizerPlugin.ChaFileInfo(null, null, DateTime.Now);
                    }

                    while (string.Equals(replacement.fileName, lastReplacementFile))
                    {
                        CharacterRandomizerPlugin.ChaFileInfo pickedFile = files[UnityEngine.Random.Range(0, files.Count - 1)];
                        replacement = new CharacterRandomizerPlugin.ChaFileInfo(pickedFile.fileName, pickedFile.charaName, pickedFile.lastUpdated);
                    }
                    return replacement;
                }
                else
                {
                    switch (charReplacementMode)
                    {
                        case ReplacementMode.CYCLIC_DATE_ASC:
                            files.Sort((fi1, fi2) => DateTime.Compare(fi1.lastUpdated, fi2.lastUpdated));
                            break;
                        case ReplacementMode.CYCLIC_DATE_DESC:
                            files.Sort((fi1, fi2) => DateTime.Compare(fi2.lastUpdated, fi1.lastUpdated));
                            break;
                        case ReplacementMode.CYCLIC_CHARA_NAME_ASC:
                            files.Sort((fi1, fi2) => string.Compare(fi1.charaName, fi2.charaName));
                            break;
                        case ReplacementMode.CYCLIC_CHARA_NAME_DESC:
                            files.Sort((fi1, fi2) => string.Compare(fi2.charaName, fi1.charaName));
                            break;
                        case ReplacementMode.CYCLIC_NAME_ASC:
                            files.Sort((fi1, fi2) => string.Compare(Path.GetFileName(fi1.fileName), Path.GetFileName(fi2.fileName)));
                            break;
                        case ReplacementMode.CYCLIC_NAME_DESC:
                            files.Sort((fi1, fi2) => string.Compare(Path.GetFileName(fi2.fileName), Path.GetFileName(fi1.fileName)));
                            break;
                    }                    

#if DEBUG
                    log.LogInfo($"Sorted Files Count: {files.Count}\nLast File:{lastReplacementFile}");
#endif

                    int cyclic = -1;
                    
                    for(int i = 0; i < files.Count; i++)
                    {
                        if (string.Equals(files[i].fileName, lastReplacementFile))
                        {
#if DEBUG
                            log.LogInfo($"Matched Current Chara {lastReplacementFile} to {files[i].fileName}");
#endif
                            cyclic = i;
                            break;
                        }
                    }
                        
                    if (cyclic < 0)
                        cyclic = 0;
                    else
                        cyclic++;

                    if (cyclic >= files.Count)
                    {
#if DEBUG
                        log.LogInfo($"Looping Cyclic Sort {charReplacementMode} Last Index: {cyclic - 1} Next Index: {0} Picked: {files[0]} ");
#endif
                        CharacterRandomizerPlugin.ChaFileInfo pickedFile = files[0];
                        pickedFile = new CharacterRandomizerPlugin.ChaFileInfo(pickedFile.fileName, pickedFile.charaName, pickedFile.lastUpdated);
                        return pickedFile;
                    }
                    else
                    {
#if DEBUG
                        log.LogInfo($"Cyclic Sort {charReplacementMode} Last Index: {cyclic - 1} Next Index: {cyclic} Picked: {files[cyclic]} ");
#endif
                        CharacterRandomizerPlugin.ChaFileInfo pickedFile = files[cyclic];
                        pickedFile = new CharacterRandomizerPlugin.ChaFileInfo(pickedFile.fileName, pickedFile.charaName, pickedFile.lastUpdated);
                        return pickedFile;
                    }
                }
            }
            catch (Exception e)
            {
                log.LogWarning($"Unable to pick replacement character: {e.Message}");
                return new CharacterRandomizerPlugin.ChaFileInfo();
            }
        }

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
#if DEBUG
            log.LogInfo($"Reloading Character {ChaControl.fileParam.fullname} Scene Load: {StudioSaveLoadApi.LoadInProgress} Scene Import: {StudioSaveLoadApi.ImportInProgress}");
#endif

            if (StudioSaveLoadApi.LoadInProgress && Loaded)
                Loaded = false;

            if (maintainState || Loaded)
                return;
            

            PluginData pluginData = GetExtendedData();
            if (pluginData != null && pluginData.data != null)
            {
#if DEBUG
                log.LogInfo($"Loading Plugin Data...");
                log.LogInfo($"{(string.Join("\n", pluginData.data.Keys.Select(s => $"{s}:{pluginData.data[s]}")))}");
#endif
                if (pluginData.data.TryGetValue("running", out var runningData)) { running = (bool)runningData; };
                if (pluginData.data.TryGetValue("charReplacementMode", out var charReplacementModeData)) { charReplacementMode = (ReplacementMode)charReplacementModeData; };
                if (pluginData.data.TryGetValue("baseDelaySeconds", out var baseDelaySecondsData)) { baseDelaySeconds = (int)baseDelaySecondsData; };
                if (pluginData.data.TryGetValue("delayVarianceRange", out var delayVarianceRangeData)) { delayVarianceRange = (int)delayVarianceRangeData; };
                if (pluginData.data.TryGetValue("subdirectory", out var subdirectoryData)) { subdirectory = (string)subdirectoryData; };
                if (pluginData.data.TryGetValue("namePattern", out var namePatternData)) { namePattern = (string)namePatternData; };
                if (pluginData.data.TryGetValue("fullAsync", out var fullAsyncData)) { fullAsync = (bool)fullAsyncData; };
                if (pluginData.data.TryGetValue("noDupes", out var noDupesData)) { noDupes = (bool)noDupesData; };
                if (pluginData.data.TryGetValue("includeChildDirectories", out var includeChildDirectoriesData)) { includeChildDirectories = (bool)includeChildDirectoriesData; };
                if (pluginData.data.TryGetValue("useSyncedTime", out var useSyncedTimeData)) { useSyncedTime = (bool)useSyncedTimeData; };
                if (pluginData.data.TryGetValue("preserveOutfit", out var preserveOutfitData)) { preserveOutfit = (bool)preserveOutfitData; };
                if (pluginData.data.TryGetValue("outfitFile", out var outfitFileData)) { outfitFile = (string)outfitFileData; };
            }
            else
            {
#if DEBUG
                log.LogInfo($"No Plugin Data...using defaults");                
#endif
                running = false;
                charReplacementMode = ReplacementMode.RANDOM;
                baseDelaySeconds = 60;
                delayVarianceRange = 30;
                subdirectory = string.IsNullOrWhiteSpace(CharacterRandomizerPlugin.DefaultSubdirectory.Value) ? "" : CharacterRandomizerPlugin.DefaultSubdirectory.Value;
                namePattern = ".*";
                fullAsync = false;
                noDupes = true;
                includeChildDirectories = false;
                useSyncedTime = true;
                preserveOutfit = false;
                outfitFile = "";
            }

            Loaded = true;
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
            PluginData pluginData = new PluginData();

            pluginData.data = new Dictionary<string, object>();

            pluginData.data["running"] = running;
            pluginData.data["charReplacementMode"] = (int)charReplacementMode;
            pluginData.data["baseDelaySeconds"] = baseDelaySeconds;
            pluginData.data["delayVarianceRange"] = delayVarianceRange;
            pluginData.data["subdirectory"] = subdirectory;
            pluginData.data["namePattern"] = namePattern;
            pluginData.data["fullAsync"] = fullAsync;
            pluginData.data["noDupes"] = noDupes;
            pluginData.data["includeChildDirectories"] = includeChildDirectories;
            pluginData.data["useSyncedTime"] = useSyncedTime;
            pluginData.data["preserveOutfit"] = preserveOutfit;
            pluginData.data["outfitFile"] = outfitFile;

#if DEBUG
            log.LogInfo($"Saving Plugin Data...");
            log.LogInfo($"{(string.Join("\n", pluginData.data.Keys.Select(s => $"{s}:{pluginData.data[s]}")))}");
#endif
            SetExtendedData(pluginData);
        }



        public enum ReplacementMode
        {
            RANDOM = 0,
            CYCLIC_DATE_ASC = 1,
            CYCLIC_DATE_DESC = 2,
            CYCLIC_NAME_ASC = 3,
            CYCLIC_NAME_DESC = 4,
            CYCLIC_CHARA_NAME_ASC = 5,
            CYCLIC_CHARA_NAME_DESC = 6
        }
    }
}
