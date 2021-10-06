using AIChara;
using BepInEx.Logging;
using KKAPI.Chara;
using KKAPI.Studio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace CharacterRandomizer
{
    public class CharacterRandomizerStudioGUI : MonoBehaviour
    {
        private static ManualLogSource Log => CharacterRandomizerPlugin.Instance.Log;

        private static Rect windowRect = new Rect(120, 220, 600, 625);
        private static readonly GUILayoutOption expandLayoutOption = GUILayout.ExpandWidth(true);

        private static GUIStyle labelStyle;
        private static GUIStyle selectedButtonStyle;

        private static bool guiLoaded = false;

        private Vector2 scrollPosition = Vector2.zero;

        public static CharacterRandomizerStudioGUI Instance;

        public static void Show()
        {
            CharacterRandomizerPlugin.Instance.StudioGUIToolbarToggle.Value = true;
        }

        public static void Hide()
        {
            CharacterRandomizerPlugin.Instance.StudioGUIToolbarToggle.Value = false;
        }


        private void Awake()
        {
            Instance = this;
            enabled = false;
        }

        private void Start()
        {
        }

        private void Update()
        {

        }

        private void OnEnable()
        {

        }

        private void OnDestroy()
        {
        }

        private ChaControl character;
        private CharacterRandomizerCharaController controller;

        private void OnGUI()
        {
            if (!guiLoaded)
            {
                labelStyle = new GUIStyle(UnityEngine.GUI.skin.label);
                selectedButtonStyle = new GUIStyle(UnityEngine.GUI.skin.button);

                selectedButtonStyle.fontStyle = FontStyle.Bold;
                selectedButtonStyle.normal.textColor = Color.red;

                labelStyle.alignment = TextAnchor.MiddleRight;
                labelStyle.normal.textColor = Color.white;

                windowRect.x = Mathf.Min(Screen.width - windowRect.width, Mathf.Max(0, windowRect.x));
                windowRect.y = Mathf.Min(Screen.height - windowRect.height, Mathf.Max(0, windowRect.y));

                guiLoaded = true;
            }

            IEnumerable<Studio.OCIChar> selectedCharacters = StudioAPI.GetSelectedCharacters();
            if (selectedCharacters.Count() > 0)
            {
                character = selectedCharacters.First().GetChaControl();
                controller = character.gameObject.GetComponent<CharacterRandomizerCharaController>();
            }
            else
            {
                character = null;
                controller = null;
            }

            KKAPI.Utilities.IMGUIUtils.DrawSolidBox(windowRect);

            string titleMessage = "Character Randomizer: ";
            if (character == null)
                titleMessage += "No Character Selected";
            else
                titleMessage += $"{character.chaFile.parameter.fullname}";


            var rect = GUILayout.Window(8820, windowRect, DoDraw, $"Character Randomizer: {titleMessage}");
            windowRect.x = rect.x;
            windowRect.y = rect.y;

            if (windowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                Input.ResetInputAxes();
        }

        private void PropagateSyncTiming()
        {
            if (!controller.UseSyncedTime)
                return;

            foreach (CharacterRandomizerCharaController randomizer in CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID).Instances)
            {
                if (randomizer.UseSyncedTime)
                {
                    randomizer.BaseDelaySeconds = controller.BaseDelaySeconds;
                    randomizer.DelayVarianceRange = controller.DelayVarianceRange;
                    randomizer.Rotation = controller.Rotation;
                }
            }
        }

        private void DoDraw(int id)
        {
            GUILayout.BeginVertical();
            {

                // Header
                GUILayout.BeginHorizontal(expandLayoutOption);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close Me", GUILayout.ExpandWidth(false))) Hide();
                GUILayout.EndHorizontal();

                if (controller != null)
                {
                    GUILayout.Label($"Replace the selected character either randomly or in sequence with the options below:");
                    GUILayout.Space(5);

                    GUILayout.BeginHorizontal();
                    controller.Running = GUILayout.Toggle(controller.Running, "  Running");
                    if (controller.Running)
                    {
                        GUILayout.Space(5);
                        GUILayout.Label($"Next Replacement In: { ((int)(controller.NextTime - Time.time))} s");
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);

                    controller.PreserveOutfit = GUILayout.Toggle(controller.PreserveOutfit, "  Preserve Outfit (Experimental - has issues with some plugins)");
                    GUILayout.Space(3);

                    if (!controller.PreserveOutfit)
                    {
                        GUILayout.Label("Coord File Name to Load - Blank for None (No Path)");
                        controller.OutfitFile = GUILayout.TextField(controller.OutfitFile, GUILayout.ExpandWidth(true));
                        GUILayout.Space(3);
                    }

                    controller.NoDupes = GUILayout.Toggle(controller.NoDupes, "  No Duplicate Characters in Scene");
                    GUILayout.Space(3);

                    bool syncTime = controller.UseSyncedTime;
                    syncTime = GUILayout.Toggle(syncTime, "  Use Sync Timers - All Sync'd Characters Share a Timer");
                    GUILayout.Space(3);

                    if (syncTime != controller.UseSyncedTime)
                    {
                        controller.UseSyncedTime = syncTime;
                        if (syncTime)
                        {
                            // Sync Delay Settings
                            CharacterRandomizerCharaController[] randomizers = GameObject.FindObjectsOfType<CharacterRandomizerCharaController>();
                            foreach (CharacterRandomizerCharaController randomizer in randomizers)
                            {
                                if (randomizer.UseSyncedTime)
                                {
                                    controller.BaseDelaySeconds = randomizer.BaseDelaySeconds;
                                    controller.DelayVarianceRange = randomizer.DelayVarianceRange;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            controller.UseSyncedTime = syncTime;
                        }
                    }


                    GUILayout.Label("Random is random, Cyclic cycles characters in the specified order.");
                    GUILayout.Label("Rotation swaps slot 1. Next cycle swaps 1 to 2 and replaces 1 again, etc. Or from last slot forward.");
                    GUILayout.Space(3);
                    controller.CharReplacementMode = (CharacterRandomizerCharaController.ReplacementMode)GUILayout.SelectionGrid((int)controller.CharReplacementMode, new string[] { "Random", "Cyclic - Last Update", "Cyclic - Last Update Desc", "Cyclic - File Name", "Cyclic - File Name Desc", "Cyclic - Chara Name", "Cyclic - Chara Name Desc" }, 3);
                    GUILayout.Space(3);
                    CharacterRandomizerCharaController.RotationMode newRotation = (CharacterRandomizerCharaController.RotationMode)GUILayout.SelectionGrid((int)controller.Rotation, new string[] { "None", "Forward", "Reverse", "Wrap Fwd", "Wrap Rev" }, 5);
                    if (newRotation != controller.Rotation)
                    {
                        controller.Rotation = newRotation;
                        PropagateSyncTiming();
                    }
                    GUILayout.Space(3);


                    GUILayout.Label($"Replacement Time is {CharacterRandomizerPlugin.MinimumDelay.Value} seconds + Base + Random seconds");
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Base Time (secs): ");
                    GUILayout.Space(5);
                    string baseDelaySecsText = controller.BaseDelaySeconds.ToString();
                    baseDelaySecsText = GUILayout.TextField(baseDelaySecsText, 8, GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Random Time (secs) From 0 to: ");
                    GUILayout.Space(5);
                    string randomDelaySecsText = controller.DelayVarianceRange.ToString();
                    randomDelaySecsText = GUILayout.TextField(randomDelaySecsText, 8, GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);

                    bool successParse = int.TryParse(baseDelaySecsText, out int baseDelaySecs);
                    if (successParse && baseDelaySecs != controller.BaseDelaySeconds)
                    {
                        controller.BaseDelaySeconds = baseDelaySecs;
                        PropagateSyncTiming();
                    }
                    successParse = int.TryParse(randomDelaySecsText, out int randomDelaySecs);
                    if (successParse && randomDelaySecs != controller.DelayVarianceRange)
                    {
                        controller.DelayVarianceRange = randomDelaySecs;
                        PropagateSyncTiming();
                    }

                    GUILayout.Label("Included Subdirectories (of your Userdata/chara/(Male/Female), | delimited: ");
                    controller.Subdirectory = GUILayout.TextField(controller.Subdirectory, GUILayout.ExpandWidth(true));
                    GUILayout.Space(3);

                    controller.IncludeChildDirectories = GUILayout.Toggle(controller.IncludeChildDirectories, " Include all children of subdirectories");
                    GUILayout.Space(3);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Character Name RegExp condition: ");
                    GUILayout.Space(5);
                    string namePatternText = controller.NamePattern;
                    namePatternText = GUILayout.TextField(namePatternText, GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(3);

                    if (IsValidRegex(namePatternText))
                        controller.NamePattern = namePatternText;

                    GUILayout.Space(5);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Refresh Char Lists"))
                        CharacterRandomizerPlugin.Instance.RefreshLists();

                    GUILayout.Space(5);
                    if (GUILayout.Button("Replace Me"))
                        controller.ReplaceCharacter();

                    GUILayout.Space(5);
                    if (GUILayout.Button("Replace All Sync'd"))
                    {
                        CharacterRandomizerPlugin.ReplaceAll();
                    }

                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label($"Select a character to set replacement options.");
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Replace All Sync'd"))
                    {
                        CharacterRandomizerPlugin.ReplaceAll();
                    }
                    GUILayout.Space(20);
                }
            }
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private static bool IsValidRegex(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            try
            {
                Regex.Match("", pattern);
            }
            catch (ArgumentException)
            {
                return false;
            }

            return true;
        }

    }
}
