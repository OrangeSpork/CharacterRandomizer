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
using System.Collections.ObjectModel;
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

        private bool randomOutfit = false;
        public bool RandomOutfit
        {
            get { return randomOutfit; }
            set { randomOutfit = value; }
        }

        private string outfitFile = ".*";
        public string OutfitFile
        {
            get { return outfitFile; }
            set { outfitFile = value; }
        }

        private string outfitDirectory = "";
        public string OutfitDirectory
        { 
            get { return outfitDirectory; }
            set { outfitDirectory = value; }
        }


        private bool preserveOutfit = false;
        public bool PreserveOutfit
        {
            get { return preserveOutfit; }
            set { preserveOutfit = value; }
        }

        private List<AccessorySuppressionSlots> accessorySuppressions = new List<AccessorySuppressionSlots>();
        public List<AccessorySuppressionSlots> AccessorySuppressions
        {
            get { return accessorySuppressions; }
            set { accessorySuppressions = value; }
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

        private int syncToSlot = -1;
        public int SyncToSlot
        {
            get { return syncToSlot; }
            set { syncToSlot = value; }
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
            if (syncToSlot == -1)
                syncToSlot = 1;
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
            if (CharReplacementMode == ReplacementMode.SYNC_TO_SLOT)
            {
                // wait a frame for the other slots to pick
                yield return null;

                // pull the character from the other slot
                replacementChaInfo = new CharacterRandomizerPlugin.ChaFileInfo(ChaControl.sex == 0 ? CharacterRandomizerPlugin.CurrentMaleCharacters[SyncToSlot] : CharacterRandomizerPlugin.CurrentFemaleCharacters[SyncToSlot], "", new DateTime());
            }
            else if (replacementChaInfo.fileName == null)
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

            List<AccessoryState> priorAccessoryState = ReadAccessoryState();
            byte[] priorClothesState = (byte[])ChaControl.fileStatus.clothesState.Clone();

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

            // Restart Animators to ensure characters are in the best replacement pose possible
            RestartAnimators();

            // Turn off shoes first - helps Heelz out
            ChaControl.SetClothesState((int)ChaFileDefine.ClothesKind.shoes, 2);
            yield return null;

            int neckState = ChaControl.neckLookCtrl.ptnNo;
            int gazeState = ChaControl.eyeLookCtrl.ptnNo;
            float skinGloss = ChaControl.skinGlossRate;

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
            else if (randomOutfit)
            {
                CharacterRandomizerPlugin.ChaFileInfo coordInfo = PickReplacementOutfit();
                if (coordInfo.fileName != null)
                {
                    yield return StartCoroutine(DoClothingLoad(coordInfo.fileName));
                }                
            }            

            yield return StartCoroutine(TickleClothesState(priorAccessoryState, priorClothesState, preserveOutfit));
            ChaControl.GetOCIChar().ChangeLookNeckPtn(neckState);
            ChaControl.GetOCIChar().ChangeLookEyesPtn(gazeState);
            ChaControl.GetOCIChar().SetTuyaRate(skinGloss);

#if DEBUG
            log.LogInfo($"Setting Last File {lastReplacementFile}");
#endif


        }     
        
        private void RestartAnimators()
        {
            Studio.OCIChar[] array = (from v in Singleton<Studio.Studio>.Instance.dicObjectCtrl
                               where v.Value.kind == 0
                               select v.Value as Studio.OCIChar).ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                array[i].RestartAnime();
            }
            Studio.OCIItem[] array2 = (from v in Singleton<Studio.Studio>.Instance.dicObjectCtrl
                                where v.Value.kind == 1
                                select v.Value as Studio.OCIItem).ToArray();
            for (int j = 0; j < array2.Length; j++)
            {
                array2[j].RestartAnime();
            }
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
        }

        private void RestoreAccessoryState(List<AccessoryState> accessoryStates)
        {
            foreach (AccessoryState state in accessoryStates)
            {
                if (state.characterAccessory)
                    continue;

                if (state.slotNumber < 20)
                {
                    if (ChaControl.nowCoordinate.accessory.parts[state.slotNumber] != null && ChaControl.infoAccessory[state.slotNumber]?.Name != null && ChaControl.infoAccessory[state.slotNumber]?.Name == state.accessoryName)
                    {
                        ChaControl.SetAccessoryState(state.slotNumber, state.visible);
#if DEBUG
                        log.LogInfo($"Set Accessory ({state.slotNumber}-{state.accessoryName}) to ({state.visible})");
#endif
                    }
                }
                else
                {
                    try
                    {
                        if (GetMoreAccessorialPartInfo(state.slotNumber - 20) != null)
                        {
                            if (GetMoreAccessorialAccInfo(state.slotNumber - 20)?.Name != null && GetMoreAccessorialAccInfo(state.slotNumber - 20)?.Name == state.accessoryName)
                            {
                                ChaControl.SetAccessoryState(state.slotNumber, state.visible);
#if DEBUG
                                log.LogInfo($"Set Accessory ({state.slotNumber}-{state.accessoryName}) to ({state.visible})");
#endif
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        private void SetStickyAccessorials(List<AccessoryState> accessoryStates)
        {
            string[] stickyAccessorials = CharacterRandomizerPlugin.StickyCharacterAccessory.Value.Split('|');
            if (stickyAccessorials.Length == 0)
                return;

            Dictionary<string, bool> stickyAccessorialSet = new Dictionary<string, bool>();
            foreach (string stickyAccessorial in stickyAccessorials)
            {
                if (accessoryStates.Any(a => a.accessoryName.ToUpper() == stickyAccessorial.ToUpper()))
                {
                    AccessoryState accState = accessoryStates.First(a => a.accessoryName.ToUpper() == stickyAccessorial.ToUpper());
                    stickyAccessorialSet.Add(accState.accessoryName, accState.visible);
                }
            }

            int i = 0;
            bool success = true;
            while (success)
            {
                try
                {
                    if (i < 20)
                    {
                        if (ChaControl.nowCoordinate.accessory.parts[i] != null && ChaControl.infoAccessory[i]?.Name != null)
                        {
                            foreach (string stickyAccessorial in stickyAccessorialSet.Keys)
                            {
                                if (ChaControl.infoAccessory[i].Name.ToUpper().Contains(stickyAccessorial.ToUpper()))
                                {
                                    ChaControl.SetAccessoryState(i, stickyAccessorialSet[stickyAccessorial]);
#if DEBUG
                                    log.LogInfo($"Set Sticky Accessory ({i}-{stickyAccessorial}) to ({stickyAccessorialSet[stickyAccessorial]})");
#endif
                                }
                            }
                        }
                    }
                    else
                    {
                        if (GetMoreAccessorialPartInfo(i - 20) != null)
                        {
                            if (GetMoreAccessorialAccInfo(i - 20)?.Name != null)
                            {
                                foreach (string stickyAccessorial in stickyAccessorialSet.Keys)
                                {
                                    if (GetMoreAccessorialAccInfo(i - 20).Name.ToUpper().Contains(stickyAccessorial.ToUpper()))
                                        ChaControl.SetAccessoryState(i, stickyAccessorialSet[stickyAccessorial]);
#if DEBUG
                                    log.LogInfo($"Set Sticky Accessory ({i}-{stickyAccessorial}) to ({stickyAccessorialSet[stickyAccessorial]})");
#endif
                                }
                            }
                        }
                        else
                        {
                            success = false;
                        }
                    }
                }
                catch (Exception)
                {
                    success = false;
                }
                finally
                {
                    i++;
                }
            }
        }

        private List<AccessoryState> ReadAccessoryState()
        {
            List<AccessoryState> accessoryStates = new List<AccessoryState>();
            int i = 0;
            bool success = true;
            while (success)
            {
                try
                {
                    if (i < 20)
                    {
                        if (ChaControl.nowCoordinate.accessory.parts[i] != null && ChaControl.infoAccessory[i]?.Name != null)
                        {
                            accessoryStates.Add(new AccessoryState(i, ChaControl.infoAccessory[i].Name, ChaControl.fileStatus.showAccessory[i], IsSlotCharacterAccessory(i)));
                        }
                    }
                    else
                    {
                        if (GetMoreAccessorialPartInfo(i - 20) != null)
                        {
                            if (GetMoreAccessorialAccInfo(i - 20)?.Name != null)
                            {
                                accessoryStates.Add(new AccessoryState(i, GetMoreAccessorialAccInfo(i - 20).Name, GetMoreAccessorySlotStatus(i - 20), IsSlotCharacterAccessory(i)));
                            }
                        }
                        else
                        {
                            success = false;
                        }
                    }
                }
                catch (Exception)
                {
                    success = false;
                }
                finally
                {
                    i++;
                }
            }
#if DEBUG
            log.LogInfo($"Read Accessory State:\n{string.Join("\n", accessoryStates)}");
#endif
            return accessoryStates;
        }

        private IEnumerator TickleClothesState(List<AccessoryState> priorAccessoryState, byte[] priorClothesState, bool restoreAllAccessories)
        {
            yield return null;
            yield return null;
            ChaControl.GetOCIChar().SetClothesState(0, ChaControl.GetOCIChar().charFileStatus.clothesState[0]);
            
            for (int i = 0; i < priorClothesState.Length; i++)
                ChaControl.GetOCIChar().SetClothesState(i, priorClothesState[i]);
            
            if (restoreAllAccessories)
                RestoreAccessoryState(priorAccessoryState);

            SuppressSelectedAccessorySlots();
            yield return null;
            SetStickyAccessorials(priorAccessoryState);
        }

        private void SuppressSelectedAccessorySlots()
        {
            if (AccessorySuppressions.Count == 0)
                return;

            int i = 0;
            bool success = true;
            while (success)
            {
                try
                {
                    ChaFileAccessory.PartsInfo parts;
                    if (i < 20)
                    {
                        parts = ChaControl.nowCoordinate.accessory.parts[i];
                    }
                    else
                    {
                        parts = GetMoreAccessorialPartInfo(i - 20);
                    }
                    if (parts == null)
                    {
                        success = false;
                    }
                    else
                    {
                        foreach (AccessorySuppressionSlots slot in AccessorySuppressions)
                        {
                            foreach (string parentKey in slotParents[slot])
                            {
                                if (parts?.parentKey == parentKey)
                                    ChaControl.SetAccessoryState(i, false);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    success = false;
                }
                finally
                {
                    i++;
                }
            }            
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

        private CharacterRandomizerPlugin.ChaFileInfo PickReplacementOutfit()
        {            
            try
            {
                List<CharacterRandomizerPlugin.ChaFileInfo> files = new List<CharacterRandomizerPlugin.ChaFileInfo>(ChaControl.sex == 1 ? CharacterRandomizerPlugin.FemaleCoordList : CharacterRandomizerPlugin.MaleCoordList);
#if DEBUG
                log.LogInfo($"Available Outfits: {files.Count}");
#endif
                Dictionary<string, bool> desiredPaths = new Dictionary<string, bool>();
                foreach (string subdir in outfitDirectory.Split(new char[] { '|' }, StringSplitOptions.None))
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

                        string desiredPath = Path.Combine((ChaControl.sex == 1 ? CharacterRandomizerPlugin.FemaleCoordDir : CharacterRandomizerPlugin.MaleCoordDir).FullName, matchSubDir);
                        log.LogInfo($"Including Replacement Outfits From Directory {desiredPath} {(includeChildrenOfSubdir ? " and child directories" : "")}");
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
                        if (!desiredPaths[path])
                            return path == Path.GetDirectoryName(fi.fileName);
                        else
                            return fi.fileName.StartsWith(path);
                    }).Count() > 0;

                });

                if (!string.IsNullOrWhiteSpace(outfitFile))
                {
                    Regex nameRegex = new Regex(outfitFile, RegexOptions.IgnoreCase);
                    files = files.FindAll(fi => nameRegex.IsMatch(fi.charaName));
                    log.LogInfo($"Filtering Replacement Outfits by Pattern {namePattern} Matches: {files.Count}");
                }

                if (files.Count == 0)
                {
                    log.LogWarning($"Cannot Replace Outfit, No Alternatives Available");
                    log.LogMessage($"Cannot Replace Outfit, No Available Matches");
                    return new CharacterRandomizerPlugin.ChaFileInfo(null, null, DateTime.Now);
                }

                CharacterRandomizerPlugin.ChaFileInfo coordFileInfo = files[UnityEngine.Random.Range(0, files.Count - 1)];
#if DEBUG
                log.LogInfo($"Picking Outfit: {coordFileInfo.charaName} {coordFileInfo.fileName}");
#endif
                return coordFileInfo;
            }
            catch (Exception e)
            {
#if DEBUG
                log.LogWarning($"Unable to pick replacement outfit: {e.Message}\n{e.StackTrace}");
                return new CharacterRandomizerPlugin.ChaFileInfo();
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
                    Regex nameRegex = new Regex(namePattern, RegexOptions.IgnoreCase);
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

                    files.RemoveAll(cfi => (!string.IsNullOrWhiteSpace(lastReplacementFile) && Path.GetFullPath(cfi.fileName) == Path.GetFullPath(lastReplacementFile)) || (noDupes && CheckCurrentCharacterRegistry(cfi.fileName)));
                    if (files.Count == 0)
                    {
                        log.LogWarning($"Cannot Replace Character, No Alternatives Available");
                        log.LogMessage($"Cannot Replace Character, No Available Matches");
                        return new CharacterRandomizerPlugin.ChaFileInfo(null, null, DateTime.Now);
                    }
                    else
                    {
                        CharacterRandomizerPlugin.ChaFileInfo pickedFile = files[UnityEngine.Random.Range(0, files.Count - 1)];
                        replacement = new CharacterRandomizerPlugin.ChaFileInfo(pickedFile.fileName, pickedFile.charaName, pickedFile.lastUpdated);
                        return replacement;
                    }
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
                if (pluginData.data.TryGetValue("randomOutfit", out var randomOutfitData)) { randomOutfit = (bool)randomOutfitData; };
                if (pluginData.data.TryGetValue("outfitFile", out var outfitFileData)) { outfitFile = (string)outfitFileData; };
                if (pluginData.data.TryGetValue("outfitDir", out var outfitDirData)) { outfitDirectory = (string)outfitDirData; };
                if (pluginData.data.TryGetValue("rotation", out var rotationData)) { rotation = (RotationMode)rotationData; };
                if (pluginData.data.TryGetValue("rotationOrder", out var rotationOrderData)) { rotationOrder = (int)rotationOrderData; }
                if (pluginData.data.TryGetValue("syncToSlot", out var syncToSlotData)) { syncToSlot = (int)syncToSlotData; }                
                if (pluginData.data.TryGetValue("accessorySuppressions", out var accessorySuppressionsData)) { accessorySuppressions = new List<AccessorySuppressionSlots>(((object[])accessorySuppressionsData).Cast<AccessorySuppressionSlots>()); }
                if (pluginData.data.TryGetValue("lastReplacementFile", out var lastReplacementFileData)) { lastReplacementFile = (string)lastReplacementFileData;  }
                if (!File.Exists(lastReplacementFile))
                    lastReplacementFile = "";

                if (!CharacterRandomizerStudioGUI.IsValidRegex(outfitFile))
                {
                    outfitFile = ".*";
                }

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
                randomOutfit = false;
                outfitDirectory = "";
                outfitFile = ".*";
            }

            Loaded = true;
        }

        protected override void OnCardBeingSaved(GameMode currentGameMode)
        {
#if DEBUG
            log.LogInfo($"Saving Chara {ChaControl.fileParam.fullname}");            
#endif
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
            pluginData.data["randomOutfit"] = randomOutfit;
            pluginData.data["outfitFile"] = outfitFile;
            pluginData.data["outfitDir"] = outfitDirectory;
            pluginData.data["rotation"] = rotation;
            pluginData.data["rotationOrder"] = rotationOrder;
            pluginData.data["syncToSlot"] = syncToSlot;
            pluginData.data["accessorySuppressions"] = accessorySuppressions.ToArray();
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
            CYCLIC_CHARA_NAME_DESC = 6,
            SYNC_TO_SLOT = 7
        }

        public enum AccessorySuppressionSlots
        {
            NECK = 0,
            WRIST = 1,
            ANKLE = 2,
            ARM = 3,
            LEG = 4,
            GLASSES = 5,
            BREASTS = 6,
            HAT = 7,
            WAIST = 8
        }
        private static readonly ReadOnlyDictionary<AccessorySuppressionSlots, string[]> slotParents = new ReadOnlyDictionary<AccessorySuppressionSlots, string[]>(new Dictionary<AccessorySuppressionSlots, string[]> {
            { AccessorySuppressionSlots.NECK, new string[] { "N_Neck", "N_Chest_f"} },
            { AccessorySuppressionSlots.WRIST, new string[] { "N_Wrist_L", "N_Wrist_R" } },
            { AccessorySuppressionSlots.ANKLE, new string[] { "N_Ankle_L", "N_Ankle_R" } },
            { AccessorySuppressionSlots.ARM, new string[] { "N_Elbo_L", "N_Arm_L", "N_Elbo_R", "N_Arm_R" } },
            { AccessorySuppressionSlots.LEG, new string[] { "N_Leg_L","N_Knee_L", "N_Leg_R", "N_Knee_R" } },
            { AccessorySuppressionSlots.GLASSES, new string[] { "N_Megane" } },
            { AccessorySuppressionSlots.BREASTS, new string[] {"N_Tikubi_L", "N_Tikubi_R" } },
            { AccessorySuppressionSlots.HAT, new string[] { "N_Head_top" } },
            { AccessorySuppressionSlots.WAIST, new string[] {  "N_Waist", "N_Waist_f", "N_Waist_b", "N_Waist_L", "N_Waist_R" } }
        });

        private static MethodInfo chaFileSaveFile = AccessTools.Method(typeof(ChaFile), "SaveFile", new Type[] { typeof(BinaryWriter), typeof(bool), typeof(int)});


        // More Accessory Helpers
        private static Type MoreAccessoriesType = Type.GetType("MoreAccessoriesAI.MoreAccessories, MoreAccessories", false);
        private static object MoreAccessoriesInstance = BepInEx.Bootstrap.Chainloader.ManagerObject.GetComponent(MoreAccessoriesType);
        private static FieldInfo additionalDataField = AccessTools.Field(MoreAccessoriesType, "_charAdditionalData");
        private static FieldInfo partsField = AccessTools.Field(MoreAccessoriesType.GetNestedType("AdditionalData", AccessTools.all), "parts");
        private static FieldInfo objectsField = AccessTools.Field(MoreAccessoriesType.GetNestedType("AdditionalData", AccessTools.all), "objects");
        private static FieldInfo showField = AccessTools.Field(MoreAccessoriesType.GetNestedType("AdditionalData", AccessTools.all).GetNestedType("AccessoryObject", AccessTools.all), "show");
        private static FieldInfo listInfoBaseField = AccessTools.Field(MoreAccessoriesType.GetNestedType("AdditionalData", AccessTools.all).GetNestedType("AccessoryObject", AccessTools.all), "info");

        private bool GetMoreAccessorySlotStatus(int slot)
        {
            IDictionary charAdditionalData = (IDictionary)additionalDataField.GetValue(MoreAccessoriesInstance);
            foreach (DictionaryEntry entry in charAdditionalData)
            {
                if (entry.Key.Equals(ChaControl.chaFile))
                {
                    IList objectList = (IList)objectsField.GetValue(entry.Value);
                    if (slot >= objectList.Count)
                    {
                        return false;
                    }
                    else
                    {
                        return (bool)showField.GetValue(objectList[slot]);
                    }
                }
            }
            return false;
        }

        private ChaFileAccessory.PartsInfo GetMoreAccessorialPartInfo(int slot)
        {
            IDictionary charAdditionalData = (IDictionary)additionalDataField.GetValue(MoreAccessoriesInstance);
            foreach (DictionaryEntry entry in charAdditionalData)
            {
                if (entry.Key.Equals(ChaControl.chaFile))
                {
                    List<ChaFileAccessory.PartsInfo> partsList = (List<ChaFileAccessory.PartsInfo>)partsField.GetValue(entry.Value);
                    if (slot >= partsList.Count)
                    {
                        return null;
                    }
                    else
                    {
                        return partsList[slot];
                    }
                }
            }
            return null;
        }

        private ListInfoBase GetMoreAccessorialAccInfo(int slot)
        {
            IDictionary charAdditionalData = (IDictionary)additionalDataField.GetValue(MoreAccessoriesInstance);
            foreach (DictionaryEntry entry in charAdditionalData)
            {
                if (entry.Key.Equals(ChaControl.chaFile))
                {
                    IList objectList = (IList)objectsField.GetValue(entry.Value);
                    if (slot >= objectList.Count)
                    {
                        return null;
                    }
                    else
                    {
                        return (ListInfoBase)listInfoBaseField.GetValue(objectList[slot]);
                    }
                }
            }
            return null;
        }

        internal struct AccessoryState
        {
            internal int slotNumber;
            internal string accessoryName;
            internal bool visible;
            internal bool characterAccessory;

            public AccessoryState(int slotNumber, string accessoryName, bool visible, bool characterAccessory)
            {
                this.slotNumber = slotNumber;
                this.accessoryName = accessoryName;
                this.visible = visible;
                this.characterAccessory = characterAccessory;
            }

            public override string ToString()
            {
                return $"({slotNumber}-{visible} {(characterAccessory ? "C":"")}): {accessoryName}";
            }
        }

        // Additional Accessory Helpers
        private static Type AdditionalAccessoryType = AccessTools.TypeByName("AdditionalAccessoryControls.AdditionalAccessoryControlsController");
        private static Type AdditionalAccessorySlotDataType = AccessTools.TypeByName("AdditionalAccessoryControls.AdditionalAccessorySlotData");
        private static PropertyInfo AdditionalAccessorySlotDataProperty = AccessTools.Property(AdditionalAccessoryType, "SlotData");
        private static PropertyInfo AdditionalAccessorySlotDataCharacterAccessoryProperty = AccessTools.Property(AdditionalAccessorySlotDataType, "CharacterAccessory");

        private bool IsSlotCharacterAccessory(int slotNumber)
        {
            try
            {
                if (AdditionalAccessoryType == null)
                    return false;
                else
                {
                    var additionalAccessoryController = ChaControl.gameObject.GetComponent(AdditionalAccessoryType);
                    if (additionalAccessoryController == null)
                        return false;

                    object[] slotData = (object[])AdditionalAccessorySlotDataProperty.GetValue(additionalAccessoryController);
                    return (bool)AdditionalAccessorySlotDataCharacterAccessoryProperty.GetValue(slotData[slotNumber]);
                }
            }
            catch { return false; }
        }
    }
}
