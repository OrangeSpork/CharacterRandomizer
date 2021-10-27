using AIChara;
using BepInEx.Logging;
using ExtensibleSaveFormat;
using HarmonyLib;
using KKAPI;
using KKAPI.Chara;
using KKAPI.Studio;
using KKAPI.Studio.SaveLoad;
using Manager;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
            get { return loaded; }
            set { loaded = value; }
        }

        // Settings

        private bool running = false;
        public bool Running
        {
            get { return running; }
            set {
                if (!running && value)
                {
                    if (!UseSyncedTime)
                        ScheduleNextReplacement();
                    else
                        StartCoroutine(ScheduleNextReplacementAsync());
                }                    

                running = value;
            }
        }

        private ReplacementMode charReplacementMode = ReplacementMode.RANDOM;
        public ReplacementMode CharReplacementMode
        {
            get { return charReplacementMode; }
            set { charReplacementMode = value; }
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
            set { delayVarianceRange = value; }
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

        private RotationMode rotation = 0;
        public RotationMode Rotation
        {
            get { return rotation; }
            set { rotation = value; }
        }

        private int rotationOrder = -1;
        public int RotationOrder
        {
            get { return rotationOrder; }
            set { rotationOrder = value; }
        }

        // vars
        private float lastReplacementTime = 0f;
        private float nextReplacementTime = 0f;

        private string lastReplacementFile = "";
        public string LastReplacementFile
        {
            get { return lastReplacementFile;  }
        }

        private ManualLogSource log => CharacterRandomizerPlugin.Instance.Log;
        private static MethodInfo onCoordinateBeingSavedMethod = AccessTools.Method(typeof(CharacterApi), "OnCoordinateBeingSaved");

        protected override void Start()
        {
            base.Start();
            if (string.IsNullOrEmpty(subdirectory))
                subdirectory = string.IsNullOrEmpty(CharacterRandomizerPlugin.DefaultSubdirectory.Value) ? "" : CharacterRandomizerPlugin.DefaultSubdirectory.Value;

            if (rotationOrder == -1)
            {                
                RotationOrder = CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances.Where(cont => cont.ChaControl.sex == ChaControl.sex).Cast<CharacterRandomizerCharaController>().Select(rand => rand.RotationOrder).Max();
                if (RotationOrder <= 0)
                    RotationOrder = 1;
                else
                    RotationOrder++;
            }
#if DEBUG
            log.LogInfo($"Assigning RotationOrder {ChaControl.fileParam.fullname} {RotationOrder}");
#endif
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
#if DEBUG
            log.LogInfo($"Cleaning up current chara registry for {RotationOrder} {ChaControl.chaFile.parameter.fullname}");
#endif
            ClearCurrentCharacterRegistry();

            base.OnDestroy();            
        }

        protected override void Update()
        {
            base.Update(); 

            if (this.running && useSyncedTime && CharacterRandomizerPlugin.NextReplacementTime == 0)
            {
                StartCoroutine(ScheduleNextReplacementAsync());
            }
            else if (this.running &&  !useSyncedTime && Time.time > nextReplacementTime)
            {
                ReplaceCharacter();
            }          
        }

        public void ReplaceCharacter(bool forceSingleReplacement = false)
        {
            if (forceSingleReplacement)
                ReplaceCharacter(new CharacterRandomizerPlugin.ChaFileInfo());
            else
                ReplaceCharacter(DetermineRotationCharacter());
        }

        public void ReplaceCharacter(CharacterRandomizerPlugin.ChaFileInfo replacementChaInfo)
        {
            StartCoroutine(DoReplaceCharacter(replacementChaInfo));
        }

        public CharacterRandomizerPlugin.ChaFileInfo DetermineRotationCharacter()
        {
            if (Rotation == RotationMode.NONE || !UseSyncedTime)
                return new CharacterRandomizerPlugin.ChaFileInfo();
            else if (ChaControl.sex == 0 ? CharacterRandomizerPlugin.RotatedMaleCharacters.Keys.Count == 0 : CharacterRandomizerPlugin.RotatedFemaleCharacters.Keys.Count == 0)
                return new CharacterRandomizerPlugin.ChaFileInfo();
            else if (ChaControl.sex == 0 & CharacterRandomizerPlugin.RotatedMaleCharacters.ContainsKey(RotationOrder))
                return new CharacterRandomizerPlugin.ChaFileInfo(CharacterRandomizerPlugin.RotatedMaleCharacters[RotationOrder], null, DateTime.Now);
            else if (ChaControl.sex == 1 & CharacterRandomizerPlugin.RotatedFemaleCharacters.ContainsKey(RotationOrder))
                return new CharacterRandomizerPlugin.ChaFileInfo(CharacterRandomizerPlugin.RotatedFemaleCharacters[RotationOrder], null, DateTime.Now);
            else
            {
#if DEBUG
                log.LogInfo($"No Rotation Available");
#endif

                return new CharacterRandomizerPlugin.ChaFileInfo();
            }
        }

        public IEnumerator DoReplaceCharacter(CharacterRandomizerPlugin.ChaFileInfo replacementChaInfo)
        { 
            if (replacementChaInfo.fileName == null)
                replacementChaInfo = PickReplacementCharacter();
#if DEBUG
            log.LogInfo($"Replacing {RotationOrder}: {ChaControl.fileParam.fullname}\nLast File:{lastReplacementFile}\nPicked Replacement: {replacementChaInfo}");
#endif
            if (string.IsNullOrEmpty(replacementChaInfo.fileName))
                yield break;

            UpdateCurrentCharacterRegistry(replacementChaInfo.fileName);

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
                catch (Exception)
                {
#if DEBUG
                    log.LogInfo($"Last Accessory Index {i}");
#endif
                    success = false;
                }
            }

            int neckState = ChaControl.neckLookCtrl.ptnNo;
            int gazeState = ChaControl.eyeLookCtrl.ptnNo;

            string fileName = "";
            if (preserveOutfit)
            {
                fileName = ChaControl.gameObject.GetInstanceID() + "-randomizer-" + RotationOrder + ".png";
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

                ChaControl.nowCoordinate.SaveFile(Path.Combine(UserData.Path, "coordinate", (ChaControl.sex == 0 ? "male" : "female"), fileName), (int)Singleton<GameSystem>.Instance.language);
                
                yield return null;
                yield return null;
                yield return null;
            }

            CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).MaintainState = true;
            ChaControl.GetOCIChar().ChangeChara(replacementChaInfo.fileName);
            CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).MaintainState = false;


            UpdateCurrentCharacterRegistry(replacementChaInfo.fileName);
            lastReplacementTime = Time.time;

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

                    if (!string.IsNullOrWhiteSpace(lastReplacementFile) && Path.GetFullPath(replacement.fileName) == Path.GetFullPath(lastReplacementFile) && files.Count == 1)
                    {
                        log.LogWarning($"Cannot Replace Character, No Alternatives Available");
                        log.LogMessage($"Cannot Replace Character, No Available Matches");
                        return new CharacterRandomizerPlugin.ChaFileInfo(null, null, DateTime.Now);
                    }
                    else if (noDupes)
                    {
                        List<CharacterRandomizerPlugin.ChaFileInfo> temp = new List<CharacterRandomizerPlugin.ChaFileInfo>(files);
                        temp.RemoveAll(fi => CheckCurrentCharacterRegistry(fi.fileName));
                        if (temp.Count == 0)
                        {
                            log.LogWarning($"Cannot Replace Character, No Alternatives Available");
                            log.LogMessage($"Cannot Replace Character, No Available Matches");
                            return new CharacterRandomizerPlugin.ChaFileInfo(null, null, DateTime.Now);
                        }
                    }

                    while (!string.IsNullOrWhiteSpace(lastReplacementFile) && Path.GetFullPath(replacement.fileName) == Path.GetFullPath(lastReplacementFile) || (noDupes && CheckCurrentCharacterRegistry(replacement.fileName)))
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
                        try
                        {
                            if (Path.GetFullPath(files[i].fileName) == Path.GetFullPath(lastReplacementFile))
                            {
#if DEBUG
                                log.LogInfo($"Matched Current Chara {lastReplacementFile} to {files[i].fileName}");
#endif
                                cyclic = i;
                                break;
                            }
                        }
                        catch
                        {

                        }
                    }

                    int originalPosition = cyclic;
#if DEBUG
                    log.LogInfo($"Current Chara Index {originalPosition} File Count {files.Count}");
#endif

                    do
                    {
                        if (cyclic >= files.Count - 1)
                        {
#if DEBUG
                            log.LogInfo($"Looping Cyclic Sort {charReplacementMode} Last Index: {cyclic} Next Index: {0} Picked: {files[0]} ");
#endif
                            cyclic = 0;

                        }
                        else
                            cyclic++;

                        CharacterRandomizerPlugin.ChaFileInfo pickedFile = files[cyclic];
                        if (!CheckCurrentCharacterRegistry(pickedFile.fileName))
                            return pickedFile;

                    } while (cyclic != originalPosition);
                    return new CharacterRandomizerPlugin.ChaFileInfo();
                }
            }
            catch (Exception e)
            {
                log.LogWarning($"Unable to pick replacement character: {e.Message}");
                return new CharacterRandomizerPlugin.ChaFileInfo();
            }
        }

        private bool CheckCurrentCharacterRegistry(string fileName)
        {
            if (ChaControl.sex == 0)
            {
                return CharacterRandomizerPlugin.CurrentMaleCharacters.Values.Contains(Path.GetFullPath(fileName));
            }
            else
            {
                return CharacterRandomizerPlugin.CurrentFemaleCharacters.Values.Contains(Path.GetFullPath(fileName));
            }
        }

        public void UpdateCurrentCharacterRegistry(string fileName)
        {
            string sanitizedFile = fileName;
            try
            {
                sanitizedFile = Path.GetFullPath(fileName);
            }            
            catch {  }

            lastReplacementFile = sanitizedFile;
            
            if (ChaControl.sex == 0)
            {
                if (!CharacterRandomizerPlugin.CurrentMaleCharacters.TryGetValue(RotationOrder, out string positionName) || positionName != fileName)
                {
                    CharacterRandomizerPlugin.CurrentMaleCharacters[RotationOrder] = sanitizedFile;
                    CharacterRandomizerPlugin.LogCurrentCharacterRegistry();
                }
            }
            else
            {
                if (!CharacterRandomizerPlugin.CurrentFemaleCharacters.TryGetValue(RotationOrder, out string positionName) || positionName != fileName)
                {
                    CharacterRandomizerPlugin.CurrentFemaleCharacters[RotationOrder] = sanitizedFile;
                    CharacterRandomizerPlugin.LogCurrentCharacterRegistry();
                }
            }
        }

        private void ClearCurrentCharacterRegistry()
        {
            CharacterRandomizerPlugin.LogCurrentCharacterRegistry();

            if (ChaControl.sex == 0)
                CharacterRandomizerPlugin.CurrentMaleCharacters.Clear();
            else
                CharacterRandomizerPlugin.CurrentFemaleCharacters.Clear();


            // Everyone higher goes down 1
            foreach (CharacterRandomizerCharaController randomizer in CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances)
            {
#if DEBUG
                log.LogInfo($"Checking {randomizer == this} {ReferenceEquals(randomizer, this)} {randomizer.ChaControl.sex} {randomizer.rotationOrder} {randomizer.ChaControl.fileParam.fullname} {randomizer.LastReplacementFile}");
#endif
                if (randomizer != this && randomizer.ChaControl.sex == ChaControl.sex && randomizer.RotationOrder > RotationOrder)
                {
                    randomizer.RotationOrder--;                    
                }

                if (randomizer != this)
                    randomizer.UpdateCurrentCharacterRegistry(randomizer.LastReplacementFile);
            }

            CharacterRandomizerPlugin.LogCurrentCharacterRegistry();
        }       

        protected override void OnReload(GameMode currentGameMode, bool maintainState)
        {
#if DEBUG
            log.LogInfo($"Reloading Character {ChaControl.fileParam.fullname} {ChaControl.chaFile.charaFileName} Last Loaded: {CharacterRandomizerPlugin.LastLoadedFile} Scene Load: {StudioSaveLoadApi.LoadInProgress} Scene Import: {StudioSaveLoadApi.ImportInProgress} Loaded: {Loaded} MS: {maintainState}");
#endif

            if (StudioSaveLoadApi.LoadInProgress && Loaded)
                Loaded = false;

            if (maintainState)
                return;


            string loadedFile = "";

            if (Path.GetFileName(CharacterRandomizerPlugin.LastLoadedFile) == ChaControl.chaFile.charaFileName)
                loadedFile = CharacterRandomizerPlugin.LastLoadedFile;
            else
            {
                // Store loaded character information
                if (ChaControl.chaFile.charaFileName != null && ChaControl.chaFile.charaFileName.Length > 0)
                {
                    FileInfo[] files = CharacterRandomizerPlugin.FemaleBaseDir.GetFiles(ChaControl.chaFile.charaFileName, SearchOption.AllDirectories);
                    if (files.Length > 0)
                        loadedFile = files[0].FullName;
                    else
                        log.LogWarning($"Unable to identify loaded character file: {ChaControl.chaFile.charaFileName}");
                }
            }

            if (rotationOrder == -1)
            {
                RotationOrder = CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances.Where(cont => cont.ChaControl.sex == ChaControl.sex).Cast<CharacterRandomizerCharaController>().Select(rand => rand.RotationOrder).Max();
                if (RotationOrder <= 0)
                    RotationOrder = 1;
                else
                    RotationOrder++;
            }
#if DEBUG
            log.LogInfo($"Assigning RotationOrder {ChaControl.fileParam.fullname} {RotationOrder}");
#endif

#if DEBUG
            log.LogInfo($"Registering Loaded Character {loadedFile}");
#endif
            UpdateCurrentCharacterRegistry(loadedFile);

            if (Loaded)
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
                if (pluginData.data.TryGetValue("noDupes", out var noDupesData)) { noDupes = (bool)noDupesData; };
                if (pluginData.data.TryGetValue("includeChildDirectories", out var includeChildDirectoriesData)) { includeChildDirectories = (bool)includeChildDirectoriesData; };
                if (pluginData.data.TryGetValue("useSyncedTime", out var useSyncedTimeData)) { useSyncedTime = (bool)useSyncedTimeData; };
                if (pluginData.data.TryGetValue("preserveOutfit", out var preserveOutfitData)) { preserveOutfit = (bool)preserveOutfitData; };
                if (pluginData.data.TryGetValue("outfitFile", out var outfitFileData)) { outfitFile = (string)outfitFileData; };
                if (pluginData.data.TryGetValue("rotation", out var rotationData)) { rotation = (RotationMode)rotationData; };
                if (pluginData.data.TryGetValue("rotationOrder", out var rotationOrderData)) { rotationOrder = (int)rotationOrderData; }
                if (pluginData.data.TryGetValue("lastReplacementFile", out var lastReplacementFileData)) { lastReplacementFile = (string)lastReplacementFileData;  }
                if (!File.Exists(lastReplacementFile))
                    lastReplacementFile = "";

                UpdateCurrentCharacterRegistry(lastReplacementFile);
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
            pluginData.data["noDupes"] = noDupes;
            pluginData.data["includeChildDirectories"] = includeChildDirectories;
            pluginData.data["useSyncedTime"] = useSyncedTime;
            pluginData.data["preserveOutfit"] = preserveOutfit;
            pluginData.data["outfitFile"] = outfitFile;
            pluginData.data["rotation"] = rotation;
            pluginData.data["rotationOrder"] = rotationOrder;
            pluginData.data["lastReplacementFile"] = lastReplacementFile;

#if DEBUG
            log.LogInfo($"Saving Plugin Data...");
            log.LogInfo($"{(string.Join("\n", pluginData.data.Keys.Select(s => $"{s}:{pluginData.data[s]}")))}");
#endif
            SetExtendedData(pluginData);
        }

        public enum RotationMode
        { 
            NONE = 0,
            FWD = 1,
            REV = 2,
            WRAP_FWD = 3,
            WRAP_REV = 4
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

        private static MethodInfo chaFileSaveFile = AccessTools.Method(typeof(ChaFile), "SaveFile", new Type[] { typeof(BinaryWriter), typeof(bool), typeof(int)});
    }
}
