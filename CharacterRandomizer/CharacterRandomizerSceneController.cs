using ExtensibleSaveFormat;
using KKAPI.Chara;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CharacterRandomizer
{
    public class CharacterRandomizerSceneController : SceneCustomFunctionController
    {
        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            if (operation == SceneOperationKind.Load)
            {
                CharacterRandomizerPlugin.CurrentMaleCharacters.Clear();
                CharacterRandomizerPlugin.CurrentFemaleCharacters.Clear();

                PluginData pluginData = GetExtendedData();
                if (pluginData != null && pluginData.data != null)
                {
                    if (pluginData.data.TryGetValue("CurrentMaleCharacters", out var savedMaleCharactersData))
                    {
                        Dictionary<object, object> savedMaleCharacters = (Dictionary<object, object>)savedMaleCharactersData;
                        if (savedMaleCharacters != null)
                        {
                            foreach (int position in savedMaleCharacters.Keys)
                            {
                                // check if we know this character
                                if (File.Exists((string)savedMaleCharacters[position]))
                                    CharacterRandomizerPlugin.CurrentMaleCharacters.Add(position, (string)savedMaleCharacters[position]);
                            }
                        }
                    }

                    if (pluginData.data.TryGetValue("CurrentFemaleCharacters", out var savedFemaleCharactersData))
                    {
                        Dictionary<object, object> savedFemaleCharacters = (Dictionary<object, object>)savedFemaleCharactersData;
                        if (savedFemaleCharacters != null)
                        {
                            foreach (int position in savedFemaleCharacters.Keys)
                            {
                                // check if we know this character
                                if (File.Exists((string)savedFemaleCharacters[position]))
                                    CharacterRandomizerPlugin.CurrentFemaleCharacters.Add(position, (string)savedFemaleCharacters[position]);
                            }
                        }
                    }
                }


                CharacterRandomizerPlugin.NextReplacementTime = 0f;
                // Clear the loaded flags
                CharacterApi.ControllerRegistration controllerRegistration = CharacterApi.GetRegisteredBehaviour(CharacterRandomizerPlugin.GUID);
                foreach (CharacterRandomizerCharaController charaController in controllerRegistration.Instances)
                {
                    if (charaController.Running)
                        charaController.ScheduleNextReplacement(true);
                }

                CharacterRandomizer.CharacterRandomizerPlugin.Instance.ScanForFolderFlags();
            }
            else if (operation == SceneOperationKind.Clear)
            {
                CharacterRandomizerPlugin.FolderRequestFlags.Clear();
                CharacterRandomizerPlugin.CurrentMaleCharacters.Clear();
                CharacterRandomizerPlugin.CurrentFemaleCharacters.Clear();
                CharacterRandomizerPlugin.NextReplacementTime = 0f;
            }
        }

        protected override void OnSceneSave()
        {
            PluginData pluginData = new PluginData();
            pluginData.data = new Dictionary<string, object>();

            pluginData.data["CurrentMaleCharacters"] = CharacterRandomizerPlugin.CurrentMaleCharacters;
            pluginData.data["CurrentFemaleCharacters"] = CharacterRandomizerPlugin.CurrentFemaleCharacters;

            CharacterRandomizerPlugin.LogCurrentCharacterRegistry();

            SetExtendedData(pluginData);

        }
    }
}
